using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharedIdentity = Matriarch.Web.Models.Identity;
using SharedRoleAssignment = Matriarch.Web.Models.RoleAssignment;
using SharedSecurityGroup = Matriarch.Web.Models.SecurityGroup;
using SharedIdentityResult = Matriarch.Web.Models.IdentityRoleAssignmentResult;
using SharedApiPermission = Matriarch.Web.Models.ApiPermission;
using Matriarch.Web.Models;
using Matriarch.Web.Configuration;

namespace Matriarch.Web.Services;

public class AzureRoleAssignmentService : IRoleAssignmentService
{
    private readonly ILogger<AzureRoleAssignmentService> _logger;
    private readonly IIdentityService _identityService;
    private readonly IGroupManagementService _groupManagementService;
    private readonly GraphServiceClient _graphClient;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";
    // Maximum page size supported by Microsoft Graph API
    private const int MaxGraphPageSize = 999;
    private const string ApplicationPermissionType = "Application";
    
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

    public AzureRoleAssignmentService(
        AppSettings settings, 
        ILogger<AzureRoleAssignmentService> logger,
        IIdentityService identityService,
        IGroupManagementService groupManagementService)
    {
        _logger = logger;
        _identityService = identityService;
        _groupManagementService = groupManagementService;

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
            var resolvedIdentity = await _identityService.ResolveIdentityAsync(identityInput);
            if (resolvedIdentity == null)
            {
                throw new InvalidOperationException($"Could not resolve identity: {identityInput}");
            }

            _logger.LogInformation("Resolved identity: {Name} ({ObjectId})", resolvedIdentity.Name, resolvedIdentity.ObjectId);

            // Step 2: Get direct group memberships
            var directGroupIds = await _groupManagementService.GetGroupMembershipsAsync(resolvedIdentity.ObjectId);
            
            // Step 3: Fetch all groups (direct and indirect) before fetching role assignments
            var (allGroupIds, groupInfoMap) = await _groupManagementService.GetAllGroupsAsync(directGroupIds);
            
            _logger.LogInformation("Found {DirectCount} direct groups and {TotalCount} total groups (including indirect)", 
                directGroupIds.Count, allGroupIds.Count);

            // Step 4: Fetch role assignments for principal and ALL groups (direct and indirect)
            var principalIds = new List<string> { resolvedIdentity.ObjectId };
            principalIds.AddRange(allGroupIds);
            
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

            // Step 5: Build security group hierarchy with role assignments using pre-fetched group info
            var securityGroups = _groupManagementService.BuildSecurityGroupsWithPreFetchedData(directGroupIds, groupInfoMap, roleAssignments);

            // Step 6: Fetch API permissions if this is a service principal
            var apiPermissions = await GetApiPermissionsAsync(resolvedIdentity.ObjectId);

            return new SharedIdentityResult
            {
                Identity = resolvedIdentity,
                DirectRoleAssignments = directRoleAssignments,
                SecurityGroups = securityGroups,
                ApiPermissions = apiPermissions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Azure");
            throw;
        }
    }

    public async Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput)
    {
        return await _identityService.SearchIdentitiesAsync(searchInput);
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

    private List<SharedSecurityGroup> BuildSecurityGroupsWithPreFetchedData(
        List<string> groupIds,
        Dictionary<string, GroupInfo> groupInfoMap,
        List<AzureRoleAssignmentDto> roleAssignments)
    {
        var securityGroups = new List<SharedSecurityGroup>();

        foreach (var groupId in groupIds)
        {
            var group = BuildSecurityGroupWithPreFetchedData(groupId, groupInfoMap, roleAssignments, new HashSet<string>());
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return securityGroups;
    }

    private SharedSecurityGroup? BuildSecurityGroupWithPreFetchedData(
        string groupId,
        Dictionary<string, GroupInfo> groupInfoMap,
        List<AzureRoleAssignmentDto> allRoleAssignments,
        HashSet<string> currentPath)
    {
        // Prevent infinite loops - if this group is in the current path, we have a circular reference
        if (currentPath.Contains(groupId))
        {
            return null;
        }

        // Check if we have info for this group
        if (!groupInfoMap.TryGetValue(groupId, out var groupInfo))
        {
            _logger.LogWarning("Group info not found for {GroupId}", groupId);
            return null;
        }

        // Add to current path for circular reference detection
        currentPath.Add(groupId);

        try
        {
            // Get role assignments for this group
            var groupRoleAssignments = allRoleAssignments
                .Where(ra => ra.PrincipalId == groupId)
                .Select(ra => new SharedRoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = groupInfo.DisplayName
                })
                .ToList();

            // Build parent groups using pre-fetched data
            var parentGroups = new List<SharedSecurityGroup>();
            foreach (var parentGroupId in groupInfo.ParentGroupIds)
            {
                // Create a new path copy for each parent branch to properly detect circular references
                var newPath = new HashSet<string>(currentPath);
                var securityParentGroup = BuildSecurityGroupWithPreFetchedData(parentGroupId, groupInfoMap, allRoleAssignments, newPath);
                if (securityParentGroup != null)
                {
                    parentGroups.Add(securityParentGroup);
                }
            }

            // Remove from current path before returning
            currentPath.Remove(groupId);

            return new SharedSecurityGroup
            {
                Id = groupInfo.Id,
                DisplayName = groupInfo.DisplayName,
                Description = groupInfo.Description,
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

    private async Task<List<SharedApiPermission>> GetApiPermissionsAsync(string principalId)
    {
        var apiPermissions = new List<SharedApiPermission>();

        try
        {
            // Try to get app role assignments for a service principal
            var appRoleAssignments = await _graphClient.ServicePrincipals[principalId]
                .AppRoleAssignments
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

            if (appRoleAssignments?.Value != null)
            {
                foreach (var assignment in appRoleAssignments.Value)
                {
                    if (assignment.ResourceId == null || assignment.AppRoleId == null)
                    {
                        continue;
                    }

                    // Get the resource service principal to find the app role details
                    try
                    {
                        var resourceSp = await _graphClient.ServicePrincipals[assignment.ResourceId.ToString()].GetAsync();
                        
                        if (resourceSp?.AppRoles != null)
                        {
                            var appRole = resourceSp.AppRoles.FirstOrDefault(r => r.Id == assignment.AppRoleId);
                            
                            apiPermissions.Add(new SharedApiPermission
                            {
                                Id = assignment.Id ?? string.Empty,
                                ResourceDisplayName = resourceSp.DisplayName ?? assignment.ResourceDisplayName ?? string.Empty,
                                ResourceId = assignment.ResourceId.ToString() ?? string.Empty,
                                PermissionType = ApplicationPermissionType,
                                PermissionValue = appRole?.Value ?? string.Empty
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not fetch resource details for app role assignment");
                        
                        // Add with limited information
                        apiPermissions.Add(new SharedApiPermission
                        {
                            Id = assignment.Id ?? string.Empty,
                            ResourceDisplayName = assignment.ResourceDisplayName ?? string.Empty,
                            ResourceId = assignment.ResourceId.ToString() ?? string.Empty,
                            PermissionType = ApplicationPermissionType,
                            PermissionValue = string.Empty
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch API permissions (identity may not be a service principal)");
        }

        return apiPermissions;
    }

    private static string EscapeODataFilterValue(string value)
    {
        // Escape single quotes to prevent OData filter injection
        return value.Replace("'", "''");
    }
}
