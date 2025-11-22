using Azure.Core;
using Azure.Identity;
using Matriarch.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharedIdentity = Matriarch.Shared.Models.Identity;
using SharedRoleAssignment = Matriarch.Shared.Models.RoleAssignment;
using SharedSecurityGroup = Matriarch.Shared.Models.SecurityGroup;
using SharedIdentityResult = Matriarch.Shared.Models.IdentityRoleAssignmentResult;

namespace Matriarch.Web.Services;

public class AzureRoleAssignmentService : IRoleAssignmentService
{
    private readonly ILogger<AzureRoleAssignmentService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";
    // Maximum page size supported by Microsoft Graph API
    private const int MaxGraphPageSize = 999;
    
    private const string _roleAssignmentsQuery = @"
        authorizationresources
        | where type =~ 'microsoft.authorization/roleassignments'
        | extend principalType = tostring(properties['principalType'])
        | extend principalId = tostring(properties['principalId'])
        | extend roleDefinitionId = tolower(tostring(properties['roleDefinitionId']))
        | extend scope = tostring(properties['scope'])
        | join kind=inner ( 
            authorizationresources
            | where type =~ 'microsoft.authorization/roledefinitions'
            | extend id = tolower(id), roleName = tostring(properties['roleName'])
        ) on $left.roleDefinitionId == $right.id
        | project id, principalId, principalType, roleDefinitionId, roleName, scope";

    public AzureRoleAssignmentService(AppSettings settings, ILogger<AzureRoleAssignmentService> logger)
    {
        _logger = logger;

        // Use ClientSecretCredential for authentication
        _credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(_credential);

        // Initialize HttpClient for direct API calls
        _httpClient = new HttpClient();
    }

    public async Task<SharedIdentityResult> GetRoleAssignmentsAsync(string identityInput)
    {
        _logger.LogInformation("Fetching role assignments from Azure for identity: {IdentityInput}", identityInput);

        try
        {
            // Step 1: Resolve and validate the identity via Entra ID
            var resolvedIdentity = await ResolveIdentityAsync(identityInput);
            if (resolvedIdentity == null)
            {
                throw new InvalidOperationException($"Could not resolve identity: {identityInput}");
            }

            _logger.LogInformation("Resolved identity: {Name} ({ObjectId})", resolvedIdentity.Name, resolvedIdentity.ObjectId);

            // Step 2: Fetch role assignments specifically for this principal and their groups
            var principalIds = new List<string> { resolvedIdentity.ObjectId };
            
            // Get group memberships first
            var groupIds = await GetGroupMembershipsAsync(resolvedIdentity.ObjectId);
            principalIds.AddRange(groupIds);

            _logger.LogInformation("Fetching role assignments for principal and {GroupCount} groups", groupIds.Count);

            // Step 3: Fetch role assignments only for these specific principals
            var roleAssignments = await FetchRoleAssignmentsForPrincipalsAsync(principalIds);
            
            // Filter direct role assignments (only for the user/service principal)
            var directRoleAssignments = roleAssignments
                .Where(ra => ra.PrincipalId == resolvedIdentity.ObjectId)
                .Select(ra => new SharedRoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = "Direct Assignment"
                })
                .ToList();

            // Step 4: Build security group hierarchy with role assignments
            var securityGroups = await BuildSecurityGroupsAsync(groupIds, roleAssignments);

            return new SharedIdentityResult
            {
                Identity = resolvedIdentity,
                DirectRoleAssignments = directRoleAssignments,
                SecurityGroups = securityGroups
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Azure");
            throw;
        }
    }

    private async Task<SharedIdentity?> ResolveIdentityAsync(string identityInput)
    {
        _logger.LogInformation("Resolving identity from input: {Input}", identityInput);

        // Auto-detect input type
        if (Guid.TryParse(identityInput, out _))
        {
            // It's a GUID - could be ObjectId or ApplicationId
            // Try as User first
            try
            {
                var user = await _graphClient.Users[identityInput].GetAsync();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? identityInput,
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as user, trying as service principal");
            }

            // Try as Service Principal
            try
            {
                var sp = await _graphClient.ServicePrincipals[identityInput].GetAsync();
                if (sp != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = sp.Id ?? identityInput,
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Identity not found by ObjectId");
            }
        }
        else if (identityInput.Contains("@"))
        {
            // It's an email - look up user by email/UPN
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var users = await _graphClient.Users.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"mail eq '{escapedInput}' or userPrincipalName eq '{escapedInput}'";
                    config.QueryParameters.Top = 1;
                });

                var user = users?.Value?.FirstOrDefault();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? identityInput,
                        Name = user.DisplayName ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User not found by email");
            }
        }
        else
        {
            // It's a display name - search by display name
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var users = await _graphClient.Users.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                    config.QueryParameters.Top = 1;
                });

                var user = users?.Value?.FirstOrDefault();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? identityInput
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as user by name, trying service principal");
            }

            // Try as Service Principal by display name
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                    config.QueryParameters.Top = 1;
                });

                var sp = sps?.Value?.FirstOrDefault();
                if (sp != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = sp.Id ?? "",
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? identityInput
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Service principal not found by name");
            }
        }

        return null;
    }

    private async Task<List<string>> GetGroupMembershipsAsync(string principalId)
    {
        var groupIds = new List<string>();

        try
        {
            // Try as user first
            var memberOfPage = await _graphClient.Users[principalId].MemberOf.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group group && group.SecurityEnabled == true && !string.IsNullOrEmpty(group.Id))
                    {
                        groupIds.Add(group.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Not a user, trying as service principal");
            
            try
            {
                // Try as service principal
                var spMemberOfPage = await _graphClient.ServicePrincipals[principalId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                if (spMemberOfPage?.Value != null)
                {
                    foreach (var directoryObject in spMemberOfPage.Value)
                    {
                        if (directoryObject is Group group && group.SecurityEnabled == true && !string.IsNullOrEmpty(group.Id))
                        {
                            groupIds.Add(group.Id);
                        }
                    }
                }
            }
            catch (Exception spEx)
            {
                _logger.LogWarning(spEx, "Could not fetch group memberships");
            }
        }

        return groupIds;
    }

    private async Task<List<AzureRoleAssignmentDto>> FetchRoleAssignmentsForPrincipalsAsync(List<string> principalIds)
    {
        if (!principalIds.Any())
        {
            return new List<AzureRoleAssignmentDto>();
        }

        _logger.LogInformation("Fetching role assignments for {Count} principals from Azure Resource Graph API...", principalIds.Count);
        
        var roleAssignments = new List<AzureRoleAssignmentDto>();
        var token = await GetAuthorizationTokenAsync();

        // Validate that all principalIds are valid GUIDs to prevent injection
        var validatedIds = principalIds.Where(id => Guid.TryParse(id, out _)).ToList();
        if (!validatedIds.Any())
        {
            _logger.LogWarning("No valid GUID principal IDs provided");
            return new List<AzureRoleAssignmentDto>();
        }

        // Build filter for specific principals (safe now that we've validated GUIDs)
        var principalFilter = string.Join(" or ", validatedIds.Select(id => $"principalId == '{id}'"));
        
        var query = $@"
            authorizationresources
            | where type =~ 'microsoft.authorization/roleassignments'
            | extend principalType = tostring(properties['principalType'])
            | extend principalId = tostring(properties['principalId'])
            | extend roleDefinitionId = tolower(tostring(properties['roleDefinitionId']))
            | extend scope = tostring(properties['scope'])
            | where {principalFilter}
            | join kind=inner ( 
                authorizationresources
                | where type =~ 'microsoft.authorization/roledefinitions'
                | extend id = tolower(id), roleName = tostring(properties['roleName'])
            ) on $left.roleDefinitionId == $right.id
            | project id, principalId, principalType, roleDefinitionId, roleName, scope";

        string? skipToken = null;

        do
        {
            var responseDocument = await FetchRoleAssignmentsPageAsync(token, skipToken, query);
            var pageOfRoleAssignments = ParseRoleAssignmentsResponse(responseDocument);
            roleAssignments.AddRange(pageOfRoleAssignments);

            skipToken = GetContinuationToken(responseDocument);
        } while (!string.IsNullOrEmpty(skipToken));

        _logger.LogInformation("Fetched {Count} role assignments for specified principals", roleAssignments.Count);
        return roleAssignments;
    }

    private async Task<List<SharedSecurityGroup>> BuildSecurityGroupsAsync(
        List<string> groupIds, 
        List<AzureRoleAssignmentDto> roleAssignments)
    {
        var securityGroups = new List<SharedSecurityGroup>();
        var allProcessedGroups = new HashSet<string>(); // Global tracking to ensure each group appears once

        foreach (var groupId in groupIds)
        {
            var group = await BuildSecurityGroupAsync(groupId, roleAssignments, allProcessedGroups, new HashSet<string>());
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return securityGroups;
    }

    private async Task<SharedSecurityGroup?> BuildSecurityGroupAsync(
        string groupId,
        List<AzureRoleAssignmentDto> allRoleAssignments,
        HashSet<string> allProcessedGroups,
        HashSet<string> currentPath)
    {
        // Prevent infinite loops - if this group is in the current path, we have a circular reference
        if (currentPath.Contains(groupId))
        {
            return null;
        }

        // If we've already fully processed this group globally, return null to avoid duplication
        if (allProcessedGroups.Contains(groupId))
        {
            return null;
        }

        // Mark this group as globally processed
        allProcessedGroups.Add(groupId);

        // Add to current path for circular reference detection
        currentPath.Add(groupId);

        try
        {
            // Get group details
            var group = await _graphClient.Groups[groupId].GetAsync();
            
            if (group == null)
            {
                return null;
            }

            // Get role assignments for this group
            var groupRoleAssignments = allRoleAssignments
                .Where(ra => ra.PrincipalId == groupId)
                .Select(ra => new SharedRoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = group.DisplayName ?? groupId
                })
                .ToList();

            // Get parent groups (groups this group is a member of)
            var parentGroups = new List<SharedSecurityGroup>();
            var memberOfPage = await _graphClient.Groups[groupId].MemberOf.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true)
                    {
                        if (!string.IsNullOrEmpty(parentGroup.Id))
                        {
                            // Use the same allProcessedGroups but a new path copy for each parent branch
                            var newPath = new HashSet<string>(currentPath);
                            var securityParentGroup = await BuildSecurityGroupAsync(parentGroup.Id, allRoleAssignments, allProcessedGroups, newPath);
                            if (securityParentGroup != null)
                            {
                                parentGroups.Add(securityParentGroup);
                            }
                        }
                    }
                }
            }

            // Remove from current path before returning
            currentPath.Remove(groupId);

            return new SharedSecurityGroup
            {
                Id = group.Id ?? groupId,
                DisplayName = group.DisplayName ?? string.Empty,
                Description = group.Description ?? string.Empty,
                RoleAssignments = groupRoleAssignments,
                ParentGroups = parentGroups
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error building security group {GroupId}", groupId);
            // Remove from current path on error as well
            currentPath.Remove(groupId);
            return null;
        }
    }

    private async Task<AccessToken> GetAuthorizationTokenAsync()
    {
        var tokenRequestContext = new TokenRequestContext(["https://management.azure.com/.default"]);
        var token = await _credential.GetTokenAsync(tokenRequestContext, default);
        return token;
    }

    private async Task<JsonDocument> FetchRoleAssignmentsPageAsync(AccessToken token, string? skipToken, string? customQuery = null)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = customQuery ?? _roleAssignmentsQuery,
            ["options"] = new Dictionary<string, object>
            {
                ["resultFormat"] = "objectArray",
                ["$top"] = 1000
            }
        };

        if (!string.IsNullOrEmpty(skipToken))
        {
            ((Dictionary<string, object>)requestBody["options"])["$skipToken"] = skipToken;
        }

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, ResourceGraphApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseContent);
    }

    private static List<AzureRoleAssignmentDto> ParseRoleAssignmentsResponse(JsonDocument responseDocument)
    {
        var roleAssignments = new List<AzureRoleAssignmentDto>();

        if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement) || 
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return roleAssignments;
        }

        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            roleAssignments.Add(new AzureRoleAssignmentDto
            {
                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                PrincipalId = row.TryGetProperty("principalId", out var principalIdProp) ? principalIdProp.GetString() ?? string.Empty : string.Empty,
                PrincipalType = row.TryGetProperty("principalType", out var principalTypeProp) ? principalTypeProp.GetString() ?? string.Empty : string.Empty,
                RoleDefinitionId = row.TryGetProperty("roleDefinitionId", out var roleDefProp) ? roleDefProp.GetString() ?? string.Empty : string.Empty,
                RoleName = row.TryGetProperty("roleName", out var roleNameProp) ? roleNameProp.GetString() ?? string.Empty : string.Empty,
                Scope = row.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? string.Empty : string.Empty
            });
        }

        return roleAssignments;
    }

    private static string? GetContinuationToken(JsonDocument responseDocument)
    {
        if (responseDocument.RootElement.TryGetProperty("$skipToken", out var skipTokenElement))
        {
            return skipTokenElement.GetString();
        }
        return null;
    }

    private static string EscapeODataFilterValue(string value)
    {
        // Escape single quotes to prevent OData filter injection
        return value.Replace("'", "''");
    }

    // DTO for Azure role assignments
    private class AzureRoleAssignmentDto
    {
        public string Id { get; set; } = string.Empty;
        public string PrincipalId { get; set; } = string.Empty;
        public string PrincipalType { get; set; } = string.Empty;
        public string RoleDefinitionId { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
    }
}
