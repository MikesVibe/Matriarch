using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
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
    private readonly IApiPermissionsService _apiPermissionsService;
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

    public AzureRoleAssignmentService(
        AppSettings settings, 
        ILogger<AzureRoleAssignmentService> logger,
        IIdentityService identityService,
        IGroupManagementService groupManagementService,
        IApiPermissionsService apiPermissionsService)
    {
        _logger = logger;
        _identityService = identityService;
        _groupManagementService = groupManagementService;
        _apiPermissionsService = apiPermissionsService;

        // Use ClientSecretCredential for authentication
        _credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

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

            var indrectGroupIds = allGroupIds.Except(directGroupIds).ToList();

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
            var securityDirectGroups = _groupManagementService.BuildSecurityGroupsWithPreFetchedData(directGroupIds, groupInfoMap, roleAssignments);
            var securityIndirectGroups = _groupManagementService.BuildSecurityGroupsWithPreFetchedData(indrectGroupIds, groupInfoMap, roleAssignments);

            // Step 6: Fetch API permissions if this is a service principal
            var apiPermissions = await _apiPermissionsService.GetApiPermissionsAsync(resolvedIdentity.ObjectId);

            return new SharedIdentityResult
            {
                Identity = resolvedIdentity,
                DirectRoleAssignments = directRoleAssignments,
                SecurityDirectGroups = securityDirectGroups,
                SecurityIndirectGroups = securityIndirectGroups,
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
}
