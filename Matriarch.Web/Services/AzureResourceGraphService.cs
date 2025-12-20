using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Matriarch.Web.Configuration;
using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for querying Azure Resource Graph API to fetch role assignments.
/// </summary>
public class AzureResourceGraphService : IResourceGraphService
{
    private readonly ILogger<AzureResourceGraphService> _logger;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";

    public AzureResourceGraphService(
        AppSettings settings,
        ILogger<AzureResourceGraphService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        // Use ClientSecretCredential for authentication
        _credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);
    }

    public async Task<List<AzureRoleAssignmentDto>> FetchRoleAssignmentsForPrincipalsAsync(List<string> principalIds)
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
            var responseDocument = await FetchRoleAssignmentsPageAsync(token, query, skipToken);
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

    private async Task<JsonDocument> FetchRoleAssignmentsPageAsync(AccessToken token, string customQuery, string? skipToken)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = customQuery,
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

    public async Task<List<KeyVaultDto>> FetchKeyVaultAccessPoliciesForPrincipalsAsync(List<string> principalIds)
    {
        if (!principalIds.Any())
        {
            return new List<KeyVaultDto>();
        }

        _logger.LogInformation("Fetching Key Vault access policies for {Count} principals from Azure Resource Graph API...", principalIds.Count);

        var keyVaults = new List<KeyVaultDto>();
        var token = await GetAuthorizationTokenAsync();

        // Validate that all principalIds are valid GUIDs to prevent injection
        var validatedIds = principalIds.Where(id => Guid.TryParse(id, out _)).ToList();
        if (!validatedIds.Any())
        {
            _logger.LogWarning("No valid GUID principal IDs provided for Key Vault query");
            return new List<KeyVaultDto>();
        }

        // Query all Key Vaults with their access policies
        var query = @"
            resources
            | where type =~ 'microsoft.keyvault/vaults'
            | extend tenantId = tostring(properties['tenantId'])
            | extend accessPolicies = properties['accessPolicies']
            | project id, name, tenantId, accessPolicies";

        string? skipToken = null;

        do
        {
            var responseDocument = await FetchKeyVaultAccessPoliciesPageAsync(token, query, skipToken);
            var pageOfKeyVaults = ParseKeyVaultAccessPoliciesResponse(responseDocument, validatedIds);
            keyVaults.AddRange(pageOfKeyVaults);

            skipToken = GetContinuationToken(responseDocument);
        } while (!string.IsNullOrEmpty(skipToken));

        _logger.LogInformation("Fetched {Count} Key Vaults with access policies for specified principals", keyVaults.Count);
        return keyVaults;
    }

    private async Task<JsonDocument> FetchKeyVaultAccessPoliciesPageAsync(AccessToken token, string customQuery, string? skipToken)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = customQuery,
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

    private static List<KeyVaultDto> ParseKeyVaultAccessPoliciesResponse(JsonDocument responseDocument, List<string> principalIds)
    {
        var keyVaults = new List<KeyVaultDto>();
        var principalIdSet = new HashSet<string>(principalIds, StringComparer.OrdinalIgnoreCase);

        if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return keyVaults;
        }

        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var keyVault = new KeyVaultDto
            {
                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                Name = row.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                TenantId = row.TryGetProperty("tenantId", out var tenantIdProp) ? tenantIdProp.GetString() ?? string.Empty : string.Empty,
                AccessPolicies = new List<AccessPolicyEntryDto>()
            };

            // Parse access policies
            if (row.TryGetProperty("accessPolicies", out var accessPoliciesProp) && 
                accessPoliciesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var policyElement in accessPoliciesProp.EnumerateArray())
                {
                    if (policyElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var objectId = policyElement.TryGetProperty("objectId", out var objIdProp) ? objIdProp.GetString() ?? string.Empty : string.Empty;
                    
                    // Only include policies for the specified principals
                    if (!string.IsNullOrEmpty(objectId) && principalIdSet.Contains(objectId))
                    {
                        var policy = new AccessPolicyEntryDto
                        {
                            TenantId = policyElement.TryGetProperty("tenantId", out var ptProp) ? ptProp.GetString() ?? string.Empty : string.Empty,
                            ObjectId = objectId,
                            ApplicationId = policyElement.TryGetProperty("applicationId", out var appIdProp) ? appIdProp.GetString() ?? string.Empty : string.Empty,
                            KeyPermissions = ParsePermissionsArray(policyElement, "permissions", "keys"),
                            SecretPermissions = ParsePermissionsArray(policyElement, "permissions", "secrets"),
                            CertificatePermissions = ParsePermissionsArray(policyElement, "permissions", "certificates"),
                            StoragePermissions = ParsePermissionsArray(policyElement, "permissions", "storage")
                        };

                        keyVault.AccessPolicies.Add(policy);
                    }
                }
            }

            // Only add Key Vault if it has at least one matching access policy
            if (keyVault.AccessPolicies.Any())
            {
                keyVaults.Add(keyVault);
            }
        }

        return keyVaults;
    }

    private static List<string> ParsePermissionsArray(JsonElement policyElement, string permissionsKey, string permissionType)
    {
        var permissions = new List<string>();

        if (policyElement.TryGetProperty(permissionsKey, out var permissionsObj) &&
            permissionsObj.ValueKind == JsonValueKind.Object &&
            permissionsObj.TryGetProperty(permissionType, out var permissionsArray) &&
            permissionsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var permission in permissionsArray.EnumerateArray())
            {
                var permValue = permission.GetString();
                if (!string.IsNullOrEmpty(permValue))
                {
                    permissions.Add(permValue);
                }
            }
        }

        return permissions;
    }
}
