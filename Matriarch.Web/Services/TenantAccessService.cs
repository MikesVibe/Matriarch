using Matriarch.Web.Configuration;
using Microsoft.Graph.Beta;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Matriarch.Web.Services;

/// <summary>
/// Service to check if a user has access to specific Azure tenants
/// </summary>
public interface ITenantAccessService
{
    Task<List<string>> GetAccessibleTenantsAsync(string userPrincipalName);
}

public class TenantAccessService : ITenantAccessService
{
    private readonly AppSettings _appSettings;
    private readonly ILogger<TenantAccessService> _logger;

    public TenantAccessService(AppSettings appSettings, ILogger<TenantAccessService> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
    }

    public async Task<List<string>> GetAccessibleTenantsAsync(string userPrincipalName)
    {
        var accessibleTenants = new List<string>();

        foreach (var tenant in _appSettings.Azure)
        {
            // Always add the tenant to the list (users can see all configured tenants)
            accessibleTenants.Add(tenant.Key);

            try
            {
                // Try to authenticate to the tenant using the service principal
                var credential = new ClientSecretCredential(
                    tenant.Value.TenantId,
                    tenant.Value.ClientId,
                    tenant.Value.ClientSecret);

                var graphClient = new GraphServiceClient(credential);

                // Try to look up the user in this tenant
                // Escape single quotes in userPrincipalName to prevent OData injection
                var escapedUserPrincipalName = userPrincipalName.Replace("'", "''");
                var userFilter = $"userPrincipalName eq '{escapedUserPrincipalName}'";
                var users = await graphClient.Users
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = userFilter;
                        requestConfiguration.QueryParameters.Select = new[] { "id", "userPrincipalName" };
                        requestConfiguration.QueryParameters.Top = 1;
                    });

                if (users?.Value?.Any() == true)
                {
                    _logger.LogInformation("User {UserPrincipalName} has access to tenant {TenantName}", userPrincipalName, tenant.Key);
                }
                else
                {
                    _logger.LogWarning("User {UserPrincipalName} not found in tenant {TenantName}, but tenant is still accessible", userPrincipalName, tenant.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify access for user {UserPrincipalName} to tenant {TenantName}, but tenant is still accessible", userPrincipalName, tenant.Key);
                // Still allow access to the tenant - verification is informational only
            }
        }

        // Return all configured tenants regardless of verification status
        return accessibleTenants;
    }
}
