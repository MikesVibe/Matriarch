using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Matriarch.Configuration;
using Matriarch.Models;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AzureRoleAssignment = Matriarch.Models.RoleAssignment;

namespace Matriarch.Services;

public class AzureDataService
{
    private readonly ILogger<AzureDataService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly CachingService _cachingService;
    private readonly List<AzureRoleAssignment> _roleAssignments = [];
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";
    private const string _query = @"
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

    public AzureDataService(AppSettings settings, ILogger<AzureDataService> logger, CachingService cachingService)
    {
        _logger = logger;
        _cachingService = cachingService;

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

    public async Task<List<AzureRoleAssignment>> FetchRoleAssignmentsAsync()
    {
        const string cacheKey = "RoleAssignments";
        
        // Try to get cached data first
        var cachedData = await _cachingService.GetCachedDataAsync<List<AzureRoleAssignment>>(cacheKey);
        if (cachedData != null)
        {
            _logger.LogInformation($"Using cached role assignments ({cachedData.Count} items)");
            return cachedData;
        }

        _logger.LogInformation("Fetching role assignments from Azure Resource Graph API for all subscriptions...");

        try
        {
            var token = await GetAuthorizationToken();

            string? skipToken = null;
            int pageCount = 0;

            do
            {
                pageCount++;
                _logger.LogInformation("Fetching page {pageCount} of role assignments...", pageCount);

                var responseDocument = await FetchRoleAssignmentsPageData(token, skipToken);
                var pageOfRoleAssignments = ParseResponse(responseDocument).ToList();
                _roleAssignments.AddRange(pageOfRoleAssignments);

                skipToken = GetContinuationToken(responseDocument);

            } while (!string.IsNullOrEmpty(skipToken));

            _logger.LogInformation("Fetched {RoleAssignmentsCount} total role assignments from all accessible subscriptions across {PageCount} page(s)", _roleAssignments.Count, pageCount);

            await _cachingService.SetCachedDataAsync(cacheKey, _roleAssignments);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching role assignments from Resource Graph API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Resource Graph API");
        }

        return _roleAssignments;
    }

    private string? GetContinuationToken(JsonDocument responseDocument)
    {
        if (responseDocument.RootElement.TryGetProperty("$skipToken", out var skipTokenElement))
        {
            _logger.LogInformation($"More results available, continuing to next page...");
            return skipTokenElement.GetString();
        }

        return null;
    }

    private async Task<AccessToken> GetAuthorizationToken()
    {
        var tokenRequestContext = new TokenRequestContext(["https://management.azure.com/.default"]);
        var token = await _credential.GetTokenAsync(tokenRequestContext, default);
        return token;
    }

    private async Task<JsonDocument> FetchRoleAssignmentsPageData(AccessToken token, string? skipToken)
    {
        // Create the request payload
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = _query,
            ["options"] = new Dictionary<string, object>
            {
                ["resultFormat"] = "objectArray",
                ["$top"] = 1000  // Maximum page size
            }
        };

        // Add skipToken if we have one (for pagination)
        if (!string.IsNullOrEmpty(skipToken))
        {
            ((Dictionary<string, object>)requestBody["options"])["$skipToken"] = skipToken;
        }

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        // Set up the HTTP request
        var request = new HttpRequestMessage(HttpMethod.Post, ResourceGraphApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = content;

        // Execute the request
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseDocument = JsonDocument.Parse(responseContent);
        return responseDocument;
    }

    private static IEnumerable<AzureRoleAssignment> ParseResponse(JsonDocument responseDocument)
    {
        if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement))
        {
            yield break;
        }
        if (dataElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            yield return new AzureRoleAssignment
            {
                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                PrincipalId = row.TryGetProperty("principalId", out var principalIdProp) ? principalIdProp.GetString() ?? string.Empty : string.Empty,
                PrincipalType = row.TryGetProperty("principalType", out var principalTypeProp) ? principalTypeProp.GetString() ?? string.Empty : string.Empty,
                RoleDefinitionId = row.TryGetProperty("roleDefinitionId", out var roleDefProp) ? roleDefProp.GetString() ?? string.Empty : string.Empty,
                RoleName = row.TryGetProperty("roleName", out var roleNameProp) ? roleNameProp.GetString() ?? string.Empty : string.Empty,
                Scope = row.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? string.Empty : string.Empty
            };
        }
    }

    public async Task<List<EnterpriseApplication>> FetchEnterpriseApplicationsAsync()
    {
        _logger.LogInformation("Fetching enterprise applications from Microsoft Graph...");
        var enterpriseApps = new List<EnterpriseApplication>();

        try
        {
            var servicePrincipals = await _graphClient.ServicePrincipals.GetAsync();
            
            if (servicePrincipals?.Value != null)
            {
                foreach (var sp in servicePrincipals.Value)
                {
                    var app = new EnterpriseApplication
                    {
                        Id = sp.Id ?? string.Empty,
                        AppId = sp.AppId ?? string.Empty,
                        DisplayName = sp.DisplayName ?? string.Empty
                    };

                    // Fetch group memberships
                    try
                    {
                        var memberOf = await _graphClient.ServicePrincipals[sp.Id].MemberOf.GetAsync();
                        if (memberOf?.Value != null)
                        {
                            app.GroupMemberships = memberOf.Value
                                .Where(m => m is Group)
                                .Select(m => m.Id ?? string.Empty)
                                .ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error fetching group memberships for {sp.DisplayName}");
                    }

                    enterpriseApps.Add(app);
                }
            }

            _logger.LogInformation($"Fetched {enterpriseApps.Count} enterprise applications");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching enterprise applications");
        }

        return enterpriseApps;
    }

    public async Task<List<AppRegistration>> FetchAppRegistrationsAsync()
    {
        _logger.LogInformation("Fetching app registrations from Microsoft Graph...");
        var appRegistrations = new List<AppRegistration>();

        try
        {
            var applications = await _graphClient.Applications.GetAsync();
            
            if (applications?.Value != null)
            {
                foreach (var app in applications.Value)
                {
                    var appReg = new AppRegistration
                    {
                        Id = app.Id ?? string.Empty,
                        AppId = app.AppId ?? string.Empty,
                        DisplayName = app.DisplayName ?? string.Empty
                    };

                    // Fetch federated credentials
                    try
                    {
                        var fedCreds = await _graphClient.Applications[app.Id]
                            .FederatedIdentityCredentials.GetAsync();
                        
                        if (fedCreds?.Value != null)
                        {
                            appReg.FederatedCredentials = fedCreds.Value.Select(fc => new FederatedCredential
                            {
                                Id = fc.Id ?? string.Empty,
                                Name = fc.Name ?? string.Empty,
                                Issuer = fc.Issuer ?? string.Empty,
                                Subject = fc.Subject ?? string.Empty,
                                Audiences = fc.Audiences?.ToList() ?? new List<string>()
                            }).ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error fetching federated credentials for {app.DisplayName}");
                    }

                    appRegistrations.Add(appReg);
                }
            }

            _logger.LogInformation($"Fetched {appRegistrations.Count} app registrations");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching app registrations");
        }

        return appRegistrations;
    }

    public async Task<List<SecurityGroup>> FetchSecurityGroupsAsync()
    {
        _logger.LogInformation("Fetching security groups from Microsoft Graph...");
        var securityGroups = new List<SecurityGroup>();

        try
        {
            var groups = await _graphClient.Groups.GetAsync(config =>
            {
                config.QueryParameters.Filter = "securityEnabled eq true";
            });
            
            if (groups?.Value != null)
            {
                foreach (var group in groups.Value)
                {
                    securityGroups.Add(new SecurityGroup
                    {
                        Id = group.Id ?? string.Empty,
                        DisplayName = group.DisplayName ?? string.Empty,
                        Description = group.Description
                    });
                }
            }

            _logger.LogInformation($"Fetched {securityGroups.Count} security groups");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching security groups");
        }

        return securityGroups;
    }
}
