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

    public async Task<SharedIdentityResult> GetRoleAssignmentsAsync(string objectId, string applicationId, string email, string name)
    {
        _logger.LogInformation("Fetching role assignments from Azure for identity: {ObjectId}", objectId);

        var identity = new SharedIdentity
        {
            ObjectId = objectId,
            ApplicationId = applicationId,
            Email = email,
            Name = name
        };

        try
        {
            // Fetch all role assignments from Azure Resource Graph
            var allRoleAssignments = await FetchRoleAssignmentsFromAzureAsync();
            
            // Filter direct role assignments for this principal
            var directRoleAssignments = allRoleAssignments
                .Where(ra => ra.PrincipalId == objectId || ra.PrincipalId == applicationId)
                .Select(ra => new SharedRoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = "Direct Assignment"
                })
                .ToList();

            // Fetch security groups this identity is a member of
            var securityGroups = await FetchSecurityGroupMembershipsAsync(objectId, applicationId, allRoleAssignments);

            return new SharedIdentityResult
            {
                Identity = identity,
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

    private async Task<List<AzureRoleAssignmentDto>> FetchRoleAssignmentsFromAzureAsync()
    {
        _logger.LogInformation("Fetching role assignments from Azure Resource Graph API...");
        
        var roleAssignments = new List<AzureRoleAssignmentDto>();
        var token = await GetAuthorizationTokenAsync();
        string? skipToken = null;

        do
        {
            var responseDocument = await FetchRoleAssignmentsPageAsync(token, skipToken);
            var pageOfRoleAssignments = ParseRoleAssignmentsResponse(responseDocument);
            roleAssignments.AddRange(pageOfRoleAssignments);

            skipToken = GetContinuationToken(responseDocument);
        } while (!string.IsNullOrEmpty(skipToken));

        _logger.LogInformation("Fetched {Count} total role assignments from Azure", roleAssignments.Count);
        return roleAssignments;
    }

    private async Task<List<SharedSecurityGroup>> FetchSecurityGroupMembershipsAsync(
        string objectId, 
        string applicationId,
        List<AzureRoleAssignmentDto> allRoleAssignments)
    {
        _logger.LogInformation("Fetching security group memberships from Microsoft Graph...");

        var securityGroups = new List<SharedSecurityGroup>();
        var processedGroups = new HashSet<string>();

        try
        {
            // Try to get user's group memberships
            var memberOfPage = await _graphClient.Users[objectId].MemberOf.GetAsync(config =>
            {
                config.QueryParameters.Top = 999;
            });

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group group && group.SecurityEnabled == true)
                    {
                        if (!string.IsNullOrEmpty(group.Id))
                        {
                            var securityGroup = await BuildSecurityGroupAsync(group.Id, allRoleAssignments, processedGroups);
                            if (securityGroup != null)
                            {
                                securityGroups.Add(securityGroup);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching group memberships for user {ObjectId}, trying service principal...", objectId);
            
            // Try as service principal if user lookup fails
            try
            {
                var spMemberOfPage = await _graphClient.ServicePrincipals[objectId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = 999;
                });

                if (spMemberOfPage?.Value != null)
                {
                    foreach (var directoryObject in spMemberOfPage.Value)
                    {
                        if (directoryObject is Group group && group.SecurityEnabled == true)
                        {
                            if (!string.IsNullOrEmpty(group.Id))
                            {
                                var securityGroup = await BuildSecurityGroupAsync(group.Id, allRoleAssignments, processedGroups);
                                if (securityGroup != null)
                                {
                                    securityGroups.Add(securityGroup);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception spEx)
            {
                _logger.LogWarning(spEx, "Error fetching group memberships for service principal {ObjectId}", objectId);
            }
        }

        return securityGroups;
    }

    private async Task<SharedSecurityGroup?> BuildSecurityGroupAsync(
        string groupId,
        List<AzureRoleAssignmentDto> allRoleAssignments,
        HashSet<string> processedGroups)
    {
        // Prevent circular references
        if (processedGroups.Contains(groupId))
        {
            return null;
        }

        processedGroups.Add(groupId);

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
                config.QueryParameters.Top = 999;
            });

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true)
                    {
                        if (!string.IsNullOrEmpty(parentGroup.Id))
                        {
                            var securityParentGroup = await BuildSecurityGroupAsync(parentGroup.Id, allRoleAssignments, processedGroups);
                            if (securityParentGroup != null)
                            {
                                parentGroups.Add(securityParentGroup);
                            }
                        }
                    }
                }
            }

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
            return null;
        }
    }

    private async Task<AccessToken> GetAuthorizationTokenAsync()
    {
        var tokenRequestContext = new TokenRequestContext(["https://management.azure.com/.default"]);
        var token = await _credential.GetTokenAsync(tokenRequestContext, default);
        return token;
    }

    private async Task<JsonDocument> FetchRoleAssignmentsPageAsync(AccessToken token, string? skipToken)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = _roleAssignmentsQuery,
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
