using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Matriarch.Configuration;
using Matriarch.Models;
using System.Text.Json;
using AzureRoleAssignment = Matriarch.Models.RoleAssignment;

namespace Matriarch.Services;

public class AzureDataService
{
    private readonly ILogger<AzureDataService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly ArmClient _armClient;
    private readonly string _subscriptionId;

    public AzureDataService(AppSettings settings, ILogger<AzureDataService> logger)
    {
        _logger = logger;
        _subscriptionId = settings.Azure.SubscriptionId;

        // Use ClientSecretCredential for authentication
        var credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(credential);

        // Initialize ARM client for role assignments and resource graph
        _armClient = new ArmClient(credential);
    }

    public async Task<List<AzureRoleAssignment>> FetchRoleAssignmentsAsync()
    {
        _logger.LogInformation("Fetching role assignments from Azure Resource Graph for entire directory...");
        var roleAssignments = new List<AzureRoleAssignment>();

        try
        {
            // Query to fetch all role assignments across the entire directory
            var query = @"
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

            var queryContent = new ResourceQueryContent(query);
            
            // Add subscription to the query scope
            queryContent.Subscriptions.Add(_subscriptionId);

            // Execute the query using Resource Graph through tenant
            var tenant = _armClient.GetTenants().First();
            var response = await tenant.GetResourcesAsync(queryContent);

            if (response?.Value?.Data != null)
            {
                // Parse the JSON response
                var dataElement = response.Value.Data.ToObjectFromJson<JsonElement>();
                
                // The Data property contains the result set directly
                // Check if it's an array (rows) or an object with columns/rows
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    // Data is directly the rows array
                    foreach (var row in dataElement.EnumerateArray())
                    {
                        // Each row can be either an array or an object
                        if (row.ValueKind == JsonValueKind.Array)
                        {
                            var rowArray = row.EnumerateArray().ToList();
                            if (rowArray.Count >= 6)
                            {
                                roleAssignments.Add(new AzureRoleAssignment
                                {
                                    Id = rowArray[0].GetString() ?? string.Empty,
                                    PrincipalId = rowArray[1].GetString() ?? string.Empty,
                                    PrincipalType = rowArray[2].GetString() ?? string.Empty,
                                    RoleDefinitionId = rowArray[3].GetString() ?? string.Empty,
                                    RoleName = rowArray[4].GetString() ?? string.Empty,
                                    Scope = rowArray[5].GetString() ?? string.Empty
                                });
                            }
                        }
                        else if (row.ValueKind == JsonValueKind.Object)
                        {
                            // Row is an object with named properties
                            roleAssignments.Add(new AzureRoleAssignment
                            {
                                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                                PrincipalId = row.TryGetProperty("principalId", out var principalIdProp) ? principalIdProp.GetString() ?? string.Empty : string.Empty,
                                PrincipalType = row.TryGetProperty("principalType", out var principalTypeProp) ? principalTypeProp.GetString() ?? string.Empty : string.Empty,
                                RoleDefinitionId = row.TryGetProperty("roleDefinitionId", out var roleDefProp) ? roleDefProp.GetString() ?? string.Empty : string.Empty,
                                RoleName = row.TryGetProperty("roleName", out var roleNameProp) ? roleNameProp.GetString() ?? string.Empty : string.Empty,
                                Scope = row.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? string.Empty : string.Empty
                            });
                        }
                    }
                }
                else if (dataElement.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
                {
                    // Data is an object with a "rows" property
                    foreach (var row in rowsElement.EnumerateArray())
                    {
                        if (row.ValueKind == JsonValueKind.Array)
                        {
                            var rowArray = row.EnumerateArray().ToList();
                            if (rowArray.Count >= 6)
                            {
                                roleAssignments.Add(new AzureRoleAssignment
                                {
                                    Id = rowArray[0].GetString() ?? string.Empty,
                                    PrincipalId = rowArray[1].GetString() ?? string.Empty,
                                    PrincipalType = rowArray[2].GetString() ?? string.Empty,
                                    RoleDefinitionId = rowArray[3].GetString() ?? string.Empty,
                                    RoleName = rowArray[4].GetString() ?? string.Empty,
                                    Scope = rowArray[5].GetString() ?? string.Empty
                                });
                            }
                        }
                        else if (row.ValueKind == JsonValueKind.Object)
                        {
                            // Row is an object with named properties
                            roleAssignments.Add(new AzureRoleAssignment
                            {
                                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                                PrincipalId = row.TryGetProperty("principalId", out var principalIdProp) ? principalIdProp.GetString() ?? string.Empty : string.Empty,
                                PrincipalType = row.TryGetProperty("principalType", out var principalTypeProp) ? principalTypeProp.GetString() ?? string.Empty : string.Empty,
                                RoleDefinitionId = row.TryGetProperty("roleDefinitionId", out var roleDefProp) ? roleDefProp.GetString() ?? string.Empty : string.Empty,
                                RoleName = row.TryGetProperty("roleName", out var roleNameProp) ? roleNameProp.GetString() ?? string.Empty : string.Empty,
                                Scope = row.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? string.Empty : string.Empty
                            });
                        }
                    }
                }
            }

            _logger.LogInformation($"Fetched {roleAssignments.Count} role assignments from entire directory");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Resource Graph");
        }

        return roleAssignments;
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
