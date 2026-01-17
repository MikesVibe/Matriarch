using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Matriarch.Web.Configuration;
using Matriarch.Web.Models;
using Matriarch.Shared.Services;

namespace Matriarch.Web.Services;

public class AzureApiPermissionsService : IApiPermissionsService
{
    private readonly ILogger<AzureApiPermissionsService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly object _lock = new object();
    private GraphServiceClient? _graphClient;
    private string? _currentTenantId;
    private const int MaxGraphPageSize = 999;
    private const string ApplicationPermissionType = "Application";

    public AzureApiPermissionsService(
        ITenantContext tenantContext,
        ILogger<AzureApiPermissionsService> logger)
    {
        _logger = logger;
        _tenantContext = tenantContext;
    }

    private GraphServiceClient GetGraphClient()
    {
        var tenantSettings = _tenantContext.GetCurrentTenantSettings();
        
        lock (_lock)
        {
            // Recreate client if tenant has changed
            if (_graphClient == null || _currentTenantId != tenantSettings.TenantId)
            {
                var cloudEnvironment = GraphClientFactory.ParseCloudEnvironment(tenantSettings.CloudEnvironment);
                
                _graphClient = GraphClientFactory.CreateClient(
                    tenantSettings.TenantId,
                    tenantSettings.ClientId,
                    tenantSettings.ClientSecret,
                    cloudEnvironment);
                    
                _currentTenantId = tenantSettings.TenantId;
            }

            return _graphClient;
        }
    }

    public async Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity)
    {
        // Groups don't have API permissions
        if (identity.Type == IdentityType.Group)
        {
            _logger.LogInformation("Skipping API permissions fetch for identity type: {IdentityType}", identity.Type);
            return new List<ApiPermission>();
        }

        var apiPermissions = new List<ApiPermission>();

        // Handle Service Principals and Managed Identities
        if (identity.Type == IdentityType.ServicePrincipal || 
            identity.Type == IdentityType.UserAssignedManagedIdentity || 
            identity.Type == IdentityType.SystemAssignedManagedIdentity)
        {
            apiPermissions.AddRange(await GetServicePrincipalApiPermissionsAsync(identity.ObjectId));
        }

        // Handle Users
        if (identity.Type == IdentityType.User)
        {
            apiPermissions.AddRange(await GetUserApiPermissionsAsync(identity.ObjectId));
        }

        return apiPermissions;
    }

    private async Task<List<ApiPermission>> GetServicePrincipalApiPermissionsAsync(string objectId)
    {
        var apiPermissions = new List<ApiPermission>();

        try
        {
            // Get app role assignments for a service principal or managed identity
            var appRoleAssignments = await GetGraphClient().ServicePrincipals[objectId]
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
                        var resourceSp = await GetGraphClient().ServicePrincipals[assignment.ResourceId.ToString()].GetAsync();
                        
                        if (resourceSp?.AppRoles != null)
                        {
                            var appRole = resourceSp.AppRoles.FirstOrDefault(r => r.Id == assignment.AppRoleId);
                            
                            apiPermissions.Add(new ApiPermission
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
                        apiPermissions.Add(new ApiPermission
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
            _logger.LogWarning(ex, "Could not fetch API permissions for Service Principal {ObjectId}", objectId);
        }

        return apiPermissions;
    }

    private async Task<List<ApiPermission>> GetUserApiPermissionsAsync(string objectId)
    {
        var apiPermissions = new List<ApiPermission>();

        try
        {
            // Get OAuth2 permission grants (delegated permissions) for the user
            var oauth2Grants = await GetGraphClient().Users[objectId]
                .Oauth2PermissionGrants
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

            if (oauth2Grants?.Value != null)
            {
                foreach (var grant in oauth2Grants.Value)
                {
                    if (string.IsNullOrEmpty(grant.ResourceId))
                    {
                        continue;
                    }

                    try
                    {
                        // Get the resource service principal to find permission details
                        var resourceSp = await GetGraphClient().ServicePrincipals[grant.ResourceId].GetAsync();
                        
                        if (!string.IsNullOrEmpty(grant.Scope))
                        {
                            // Scope contains space-separated permission names
                            var scopes = grant.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            
                            foreach (var scope in scopes)
                            {
                                apiPermissions.Add(new ApiPermission
                                {
                                    Id = grant.Id ?? string.Empty,
                                    ResourceDisplayName = resourceSp?.DisplayName ?? "Unknown Resource",
                                    ResourceId = grant.ResourceId,
                                    PermissionType = "Delegated",
                                    PermissionValue = scope
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not fetch resource details for OAuth2 permission grant");
                    }
                }
            }

            // Also get app role assignments for the user (if assigned to applications)
            var userAppRoleAssignments = await GetGraphClient().Users[objectId]
                .AppRoleAssignments
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

            if (userAppRoleAssignments?.Value != null)
            {
                foreach (var assignment in userAppRoleAssignments.Value)
                {
                    if (assignment.ResourceId == null || assignment.AppRoleId == null)
                    {
                        continue;
                    }

                    try
                    {
                        var resourceSp = await GetGraphClient().ServicePrincipals[assignment.ResourceId.ToString()].GetAsync();
                        
                        if (resourceSp?.AppRoles != null)
                        {
                            var appRole = resourceSp.AppRoles.FirstOrDefault(r => r.Id == assignment.AppRoleId);
                            
                            // Only add if it's not a default role (to avoid duplicates)
                            if (appRole != null && !string.IsNullOrEmpty(appRole.Value))
                            {
                                apiPermissions.Add(new ApiPermission
                                {
                                    Id = assignment.Id ?? string.Empty,
                                    ResourceDisplayName = resourceSp.DisplayName ?? "Unknown Resource",
                                    ResourceId = assignment.ResourceId.ToString() ?? string.Empty,
                                    PermissionType = "User Role",
                                    PermissionValue = appRole.Value ?? string.Empty
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not fetch resource details for user app role assignment");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch API permissions for User {ObjectId}", objectId);
        }

        return apiPermissions;
    }
}
