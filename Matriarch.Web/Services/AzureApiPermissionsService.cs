using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Matriarch.Web.Configuration;
using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public class AzureApiPermissionsService : IApiPermissionsService
{
    private readonly ILogger<AzureApiPermissionsService> _logger;
    private readonly GraphServiceClient _graphClient;
    private const int MaxGraphPageSize = 999;
    private const string ApplicationPermissionType = "Application";

    public AzureApiPermissionsService(
        AppSettings settings,
        ILogger<AzureApiPermissionsService> logger)
    {
        _logger = logger;

        // Use ClientSecretCredential for authentication
        var credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(credential);
    }

    public async Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity)
    {
        // Users and Groups don't have API permissions (app role assignments)
        // Only Service Principals and Managed Identities can have API permissions
        if (identity.Type == IdentityType.User || identity.Type == IdentityType.Group)
        {
            _logger.LogInformation("Skipping API permissions fetch for identity type: {IdentityType}", identity.Type);
            return new List<ApiPermission>();
        }

        var apiPermissions = new List<ApiPermission>();

        try
        {
            // Try to get app role assignments for a service principal or managed identity
            var appRoleAssignments = await _graphClient.ServicePrincipals[identity.ObjectId]
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
            _logger.LogWarning(ex, "Could not fetch API permissions for {IdentityType} {ObjectId}", 
                identity.Type, identity.ObjectId);
        }

        return apiPermissions;
    }
}
