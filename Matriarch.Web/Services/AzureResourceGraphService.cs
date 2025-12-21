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
    private readonly ITenantContext _tenantContext;
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly object _lock = new object();
    private TokenCredential? _credential;
    private string? _currentTenantId;
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";
    
    // Global throttling state - shared across all instances and threads
    private static readonly SemaphoreSlim _throttleSemaphore = new SemaphoreSlim(1, 1);
    private static DateTime _throttleUntil = DateTime.MinValue;
    private static readonly object _throttleLock = new object();

    public AzureResourceGraphService(
        AppSettings settings,
        ITenantContext tenantContext,
        ILogger<AzureResourceGraphService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _settings = settings;
        _tenantContext = tenantContext;
    }

    private TokenCredential GetCredential()
    {
        var tenantSettings = _tenantContext.GetCurrentTenantSettings();
        
        lock (_lock)
        {
            // Recreate credential if tenant has changed
            if (_credential == null || _currentTenantId != tenantSettings.TenantId)
            {
                _credential = new ClientSecretCredential(
                    tenantSettings.TenantId,
                    tenantSettings.ClientId,
                    tenantSettings.ClientSecret);
                _currentTenantId = tenantSettings.TenantId;
            }

            return _credential;
        }
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
        var token = await GetCredential().GetTokenAsync(tokenRequestContext, default);
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

        var response = await SendRequestWithRetryAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();
        response.Dispose();
        
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

    private async Task<HttpResponseMessage> SendRequestWithRetryAsync(HttpRequestMessage request)
    {
        var maxRetries = _settings.Parallelization.MaxRetryAttempts;
        var baseDelay = _settings.Parallelization.RetryDelayMilliseconds;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            // Check if we're in a global throttle period
            await WaitForThrottleClearanceAsync();
            
            try
            {
                // Clone the request for retry attempts (HttpRequestMessage can only be sent once)
                var requestClone = await CloneHttpRequestMessageAsync(request);
                var response = await _httpClient.SendAsync(requestClone);

                // If we get a 429 or 503, set global throttle and retry with exponential backoff
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                    response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt < maxRetries)
                    {
                        // Extract retry-after header if available
                        var retryAfterMs = response.Headers.RetryAfter?.Delta?.TotalMilliseconds ?? 
                                       (baseDelay * Math.Pow(2, attempt));
                        
                        // Set global throttle to block all parallel requests
                        SetGlobalThrottle(retryAfterMs);
                        
                        _logger.LogWarning(
                            "Received {StatusCode} from Azure Resource Graph API. Global throttle activated for {DelayMs}ms. Retry attempt {Attempt}/{MaxRetries}.",
                            (int)response.StatusCode, retryAfterMs, attempt + 1, maxRetries);
                        
                        response.Dispose();
                        
                        // Wait for the throttle period
                        await Task.Delay(TimeSpan.FromMilliseconds(retryAfterMs));
                        continue;
                    }
                    else
                    {
                        _logger.LogError(
                            "Received {StatusCode} from Azure Resource Graph API after {MaxRetries} retries. Failing request.",
                            (int)response.StatusCode, maxRetries);
                        response.EnsureSuccessStatusCode(); // This will throw
                    }
                }

                // Success - clear any throttle state
                ClearGlobalThrottle();
                
                // For other status codes, ensure success or throw
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var delayMs = baseDelay * Math.Pow(2, attempt);
                
                _logger.LogWarning(ex, 
                    "HTTP request failed. Retry attempt {Attempt}/{MaxRetries}. Waiting {DelayMs}ms before retry.",
                    attempt + 1, maxRetries, delayMs);
                
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
            }
        }

        // Should not reach here, but if we do, make one final attempt
        var finalRequest = await CloneHttpRequestMessageAsync(request);
        return await _httpClient.SendAsync(finalRequest);
    }

    private async Task WaitForThrottleClearanceAsync()
    {
        while (true)
        {
            DateTime throttleUntil;
            lock (_throttleLock)
            {
                throttleUntil = _throttleUntil;
            }

            if (throttleUntil <= DateTime.UtcNow)
            {
                // Throttle period has passed
                return;
            }

            var waitTime = throttleUntil - DateTime.UtcNow;
            if (waitTime.TotalMilliseconds > 0)
            {
                _logger.LogInformation(
                    "Waiting for global throttle clearance. Time remaining: {WaitMs}ms",
                    waitTime.TotalMilliseconds);
                
                await Task.Delay(waitTime);
            }
        }
    }

    private void SetGlobalThrottle(double delayMs)
    {
        lock (_throttleLock)
        {
            var throttleUntil = DateTime.UtcNow.AddMilliseconds(delayMs);
            
            // Only update if the new throttle period extends beyond the current one
            if (throttleUntil > _throttleUntil)
            {
                _throttleUntil = throttleUntil;
                _logger.LogWarning(
                    "Global throttle set until {ThrottleUntil} (UTC). All Azure Resource Graph API requests will wait.",
                    _throttleUntil);
            }
        }
    }

    private void ClearGlobalThrottle()
    {
        lock (_throttleLock)
        {
            if (_throttleUntil > DateTime.MinValue)
            {
                _throttleUntil = DateTime.MinValue;
                _logger.LogInformation("Global throttle cleared. Azure Resource Graph API is accessible.");
            }
        }
    }

    private async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
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

        var response = await SendRequestWithRetryAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();
        response.Dispose();
        
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

    public async Task<List<ManagedIdentityResourceDto>> FetchManagedIdentitiesByTagAsync(string tagValue)
    {
        if (string.IsNullOrWhiteSpace(tagValue))
        {
            _logger.LogWarning("Tag value is empty");
            return new List<ManagedIdentityResourceDto>();
        }

        _logger.LogInformation("Fetching managed identities for tag value: {TagValue}", tagValue);

        var managedIdentities = new List<ManagedIdentityResourceDto>();
        var token = await GetAuthorizationTokenAsync();

        // Sanitize tag value to prevent injection - allow only alphanumeric, hyphens, underscores
        var sanitizedTag = new string(tagValue.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (string.IsNullOrEmpty(sanitizedTag))
        {
            _logger.LogWarning("Tag value contains no valid characters");
            return new List<ManagedIdentityResourceDto>();
        }

        // Query from problem statement - updated to use parameterized tag value
        var query = $@"
(
    ResourceContainers
    | where type =~ 'microsoft.resources/subscriptions/resourcegroups'
    | where tolower(tostring(tags['backstage-owner-ref'])) == '{sanitizedTag.ToLower()}'
    | project subscriptionId, resourceGroup = name
    | join kind=inner (
        Resources
        | where isnotempty(identity)
        | where identity.type has 'SystemAssigned'
        | project subscriptionId,
                  resourceGroup,
                  resourceId = id,
                  resourceName = name,
                  resourceType = type,
                  principalId = tostring(identity.principalId),
                  tenantId = tostring(identity.tenantId)
    ) on subscriptionId, resourceGroup
    | project subscriptionId,
              resourceGroup,
              resourceId,
              resourceName,
              resourceType,
              identityType = 'SystemAssigned',
              principalId,
              tenantId,
              managedIdentityResourceId = ''
)
| union (
    ResourceContainers
    | where type =~ 'microsoft.resources/subscriptions/resourcegroups'
    | where tolower(tostring(tags['backstage-owner-ref'])) == '{sanitizedTag.ToLower()}'
    | project subscriptionId, resourceGroup = name
    | join kind=inner (
        Resources
        | where isnotempty(identity)
        | where identity.type has 'UserAssigned'
        | mv-expand userAssignedIdentityId = bag_keys(identity.userAssignedIdentities)
        | extend userAssignedIdentityId = tostring(userAssignedIdentityId)
        | extend userAssignedIdentity = identity.userAssignedIdentities[userAssignedIdentityId]
        | project subscriptionId,
                  resourceGroup,
                  resourceId = id,
                  resourceName = name,
                  resourceType = type,
                  userAssignedIdentityId,
                  principalId = tostring(userAssignedIdentity.principalId),
                  tenantId = tostring(userAssignedIdentity.tenantId)
    ) on subscriptionId, resourceGroup
    | project subscriptionId,
              resourceGroup,
              resourceId,
              resourceName,
              resourceType,
              identityType = 'UserAssigned',
              principalId,
              tenantId,
              managedIdentityResourceId = userAssignedIdentityId
)
| order by subscriptionId, resourceGroup, resourceName";

        string? skipToken = null;

        do
        {
            var responseDocument = await FetchManagedIdentitiesPageAsync(token, query, skipToken);
            var pageOfIdentities = ParseManagedIdentitiesResponse(responseDocument);
            managedIdentities.AddRange(pageOfIdentities);

            skipToken = GetContinuationToken(responseDocument);
        } while (!string.IsNullOrEmpty(skipToken));

        _logger.LogInformation("Fetched {Count} managed identities for tag value {TagValue}", managedIdentities.Count, tagValue);
        return managedIdentities;
    }

    private async Task<JsonDocument> FetchManagedIdentitiesPageAsync(AccessToken token, string customQuery, string? skipToken)
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

        var response = await SendRequestWithRetryAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();
        response.Dispose();
        
        return JsonDocument.Parse(responseContent);
    }

    private static List<ManagedIdentityResourceDto> ParseManagedIdentitiesResponse(JsonDocument responseDocument)
    {
        var identities = new List<ManagedIdentityResourceDto>();

        if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return identities;
        }

        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            identities.Add(new ManagedIdentityResourceDto
            {
                SubscriptionId = row.TryGetProperty("subscriptionId", out var subProp) ? subProp.GetString() ?? string.Empty : string.Empty,
                ResourceGroup = row.TryGetProperty("resourceGroup", out var rgProp) ? rgProp.GetString() ?? string.Empty : string.Empty,
                ResourceId = row.TryGetProperty("resourceId", out var ridProp) ? ridProp.GetString() ?? string.Empty : string.Empty,
                ResourceName = row.TryGetProperty("resourceName", out var rnProp) ? rnProp.GetString() ?? string.Empty : string.Empty,
                ResourceType = row.TryGetProperty("resourceType", out var rtProp) ? rtProp.GetString() ?? string.Empty : string.Empty,
                IdentityType = row.TryGetProperty("identityType", out var itProp) ? itProp.GetString() ?? string.Empty : string.Empty,
                PrincipalId = row.TryGetProperty("principalId", out var pidProp) ? pidProp.GetString() ?? string.Empty : string.Empty,
                TenantId = row.TryGetProperty("tenantId", out var tidProp) ? tidProp.GetString() ?? string.Empty : string.Empty,
                ManagedIdentityResourceId = row.TryGetProperty("managedIdentityResourceId", out var miridProp) ? miridProp.GetString() ?? string.Empty : string.Empty
            });
        }

        return identities;
    }
}
