using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Matriarch.Configuration;
using Matriarch.Models;
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

        // Initialize ARM client for role assignments
        _armClient = new ArmClient(credential);
    }

    public async Task<List<AzureRoleAssignment>> FetchRoleAssignmentsAsync()
    {
        _logger.LogInformation("Fetching role assignments from Azure...");
        var roleAssignments = new List<AzureRoleAssignment>();

        try
        {
            // Get subscription scope
            var subscriptionId = ResourceIdentifier.Parse($"/subscriptions/{_subscriptionId}");
            var subscription = _armClient.GetSubscriptionResource(subscriptionId);

            // Fetch role assignments at subscription level
            await foreach (var roleAssignment in subscription.GetRoleAssignments().GetAllAsync())
            {
                var properties = roleAssignment.Data;
                var roleDefinitionId = properties.RoleDefinitionId?.ToString() ?? string.Empty;
                
                // Extract role name from the role definition ID (last segment)
                string roleName = string.Empty;
                if (!string.IsNullOrEmpty(roleDefinitionId))
                {
                    var segments = roleDefinitionId.Split('/');
                    roleName = segments.Length > 0 ? segments[^1] : roleDefinitionId;
                }

                roleAssignments.Add(new AzureRoleAssignment
                {
                    Id = properties.Id?.ToString() ?? string.Empty,
                    PrincipalId = properties.PrincipalId?.ToString() ?? string.Empty,
                    PrincipalType = properties.PrincipalType?.ToString() ?? string.Empty,
                    RoleDefinitionId = roleDefinitionId,
                    RoleName = roleName,
                    Scope = properties.Scope ?? string.Empty
                });
            }

            _logger.LogInformation($"Fetched {roleAssignments.Count} role assignments");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments");
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
