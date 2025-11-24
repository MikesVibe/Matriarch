using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Matriarch.Web.Models;
using Matriarch.Web.Configuration;
using SharedIdentity = Matriarch.Web.Models.Identity;

namespace Matriarch.Web.Services;

public interface IIdentityService
{
    Task<SharedIdentity?> ResolveIdentityAsync(string identityInput);
    Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput);
}

public class IdentityService : IIdentityService
{
    private readonly ILogger<IdentityService> _logger;
    private readonly GraphServiceClient _graphClient;
    private const int MaxGraphPageSize = 999;

    public IdentityService(AppSettings settings, ILogger<IdentityService> logger)
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

    public async Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput)
    {
        _logger.LogInformation("Searching for identities matching: {SearchInput}", searchInput);

        var identities = new List<SharedIdentity>();

        try
        {
            // If it's a GUID, try exact match lookups (will return at most one result per type)
            if (Guid.TryParse(searchInput, out _))
            {
                var identity = await ResolveIdentityAsync(searchInput);
                if (identity != null)
                {
                    identities.Add(identity);
                }
            }
            else if (searchInput.Contains("@"))
            {
                // Email search - may return one user
                var identity = await ResolveIdentityAsync(searchInput);
                if (identity != null)
                {
                    identities.Add(identity);
                }
            }
            else
            {
                // Display name search - can return multiple results
                identities.AddRange(await SearchByDisplayNameAsync(searchInput));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for identities");
        }

        return new IdentitySearchResult
        {
            Identities = identities
        };
    }

    private async Task<List<SharedIdentity>> SearchByDisplayNameAsync(string displayName)
    {
        var identities = new List<SharedIdentity>();
        var escapedInput = EscapeODataFilterValue(displayName);

        // Search for users
        try
        {
            var users = await _graphClient.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                config.QueryParameters.Top = 10; // Limit to 10 results
            });

            if (users?.Value != null)
            {
                foreach (var user in users.Value)
                {
                    identities.Add(new SharedIdentity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? "",
                        Type = IdentityType.User
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching users by display name");
        }

        // Search for service principals
        try
        {
            var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                config.QueryParameters.Top = 10; // Limit to 10 results
            });

            if (sps?.Value != null)
            {
                foreach (var sp in sps.Value)
                {
                    var identity = await CreateIdentityFromServicePrincipalAsync(sp);
                    identities.Add(identity);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching service principals by display name");
        }

        // Search for groups
        try
        {
            var groups = await _graphClient.Groups.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}') and securityEnabled eq true";
                config.QueryParameters.Top = 10; // Limit to 10 results
            });

            if (groups?.Value != null)
            {
                foreach (var group in groups.Value)
                {
                    identities.Add(new SharedIdentity
                    {
                        ObjectId = group.Id ?? "",
                        ApplicationId = "",
                        Email = "",
                        Name = group.DisplayName ?? "",
                        Type = IdentityType.Group
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching groups by display name");
        }

        return identities;
    }

    public async Task<SharedIdentity?> ResolveIdentityAsync(string identityInput)
    {
        _logger.LogInformation("Resolving identity from input: {Input}", identityInput);

        // Auto-detect input type
        if (Guid.TryParse(identityInput, out _))
        {
            // It's a GUID - could be ObjectId or ApplicationId
            // First, try to resolve by ObjectId using DirectoryObjects.GetByIds for efficiency
            try
            {
                var getByIdsRequest = new Microsoft.Graph.Beta.DirectoryObjects.GetByIds.GetByIdsPostRequestBody
                {
                    Ids = new List<string> { identityInput },
                    Types = new List<string> { "user", "servicePrincipal", "application", "group" }
                };

                var response = await _graphClient.DirectoryObjects.GetByIds.PostAsGetByIdsPostResponseAsync(getByIdsRequest);
                var directoryObjects = response?.Value;

                if (directoryObjects != null && directoryObjects.Count > 0)
                {
                    var directoryObject = directoryObjects[0];
                    
                    // Check the type and cast appropriately
                    if (directoryObject is User user)
                    {
                        _logger.LogInformation("Found User by ObjectId: {ObjectId}", identityInput);
                        return new SharedIdentity
                        {
                            ObjectId = user.Id ?? identityInput,
                            ApplicationId = "",
                            Email = user.Mail ?? user.UserPrincipalName ?? "",
                            Name = user.DisplayName ?? "",
                            Type = IdentityType.User
                        };
                    }
                    else if (directoryObject is ServicePrincipal sp)
                    {
                        _logger.LogInformation("Found Service Principal by ObjectId: {ObjectId}", identityInput);
                        return await CreateIdentityFromServicePrincipalAsync(sp);
                    }
                    else if (directoryObject is Microsoft.Graph.Beta.Models.Application app)
                    {
                        _logger.LogInformation("Found App Registration by ObjectId: {ObjectId}, looking for Enterprise Application", identityInput);
                        
                        // Find the corresponding Service Principal (Enterprise Application) using the AppId
                        if (!string.IsNullOrEmpty(app.AppId))
                        {
                            var escapedAppId = EscapeODataFilterValue(app.AppId);
                            var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
                            {
                                config.QueryParameters.Filter = $"appId eq '{escapedAppId}'";
                                config.QueryParameters.Top = 1;
                            });

                            var servicePrincipal = sps?.Value?.FirstOrDefault();
                            if (servicePrincipal != null)
                            {
                                var identity = await CreateIdentityFromServicePrincipalAsync(servicePrincipal);
                                // Override the AppRegistrationId since we know it from the App object
                                identity.AppRegistrationId = app.Id;
                                // Also ensure the name is set correctly
                                if (string.IsNullOrEmpty(identity.Name))
                                {
                                    identity.Name = app.DisplayName ?? "";
                                }
                                return identity;
                            }
                            else
                            {
                                _logger.LogWarning("Found App Registration (ObjectId: {ObjectId}, AppId: {AppId}) but no corresponding Enterprise Application exists in this tenant", app.Id, app.AppId);
                            }
                        }
                    }
                    else if (directoryObject is Group group)
                    {
                        if (group.SecurityEnabled == true)
                        {
                            _logger.LogInformation("Found Security Group by ObjectId: {ObjectId}", identityInput);
                            return new SharedIdentity
                            {
                                ObjectId = group.Id ?? identityInput,
                                ApplicationId = "",
                                Email = "",
                                Name = group.DisplayName ?? "",
                                Type = IdentityType.Group
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found by ObjectId using GetByIds, trying as ApplicationId");
            }

            // If not found by ObjectId, try as ApplicationId (AppId)
            // Note: GetByIds doesn't support ApplicationId, so we need a separate query
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"appId eq '{escapedInput}'";
                    config.QueryParameters.Top = 1;
                });

                var sp = sps?.Value?.FirstOrDefault();
                if (sp != null)
                {
                    _logger.LogInformation("Found Enterprise Application by Application ID: {AppId}", identityInput);
                    return await CreateIdentityFromServicePrincipalAsync(sp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Identity not found by GUID (tried both ObjectId and ApplicationId)");
            }
        }
        else if (identityInput.Contains("@"))
        {
            // It's an email - look up user by email/UPN
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var users = await _graphClient.Users.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"mail eq '{escapedInput}' or userPrincipalName eq '{escapedInput}'";
                    config.QueryParameters.Top = 1;
                });

                var user = users?.Value?.FirstOrDefault();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? identityInput,
                        Name = user.DisplayName ?? "",
                        Type = IdentityType.User
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User not found by email");
            }
        }
        else
        {
            // It's a display name - search by display name
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var users = await _graphClient.Users.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                    config.QueryParameters.Top = 1;
                });

                var user = users?.Value?.FirstOrDefault();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? identityInput,
                        Type = IdentityType.User
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as user by name, trying service principal");
            }

            // Try as Service Principal by display name
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}')";
                    config.QueryParameters.Top = 1;
                });

                var sp = sps?.Value?.FirstOrDefault();
                if (sp != null)
                {
                    return await CreateIdentityFromServicePrincipalAsync(sp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Service principal not found by name, trying group");
            }

            // Try as Group by display name
            try
            {
                var escapedInput = EscapeODataFilterValue(identityInput);
                var groups = await _graphClient.Groups.GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"startswith(displayName, '{escapedInput}') and securityEnabled eq true";
                    config.QueryParameters.Top = 1;
                });

                var group = groups?.Value?.FirstOrDefault();
                if (group != null)
                {
                    _logger.LogInformation("Found Security Group by name: {GroupName}", identityInput);
                    return new SharedIdentity
                    {
                        ObjectId = group.Id ?? "",
                        ApplicationId = "",
                        Email = "",
                        Name = group.DisplayName ?? identityInput,
                        Type = IdentityType.Group
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Group not found by name");
            }
        }

        _logger.LogWarning("Identity could not be resolved from input: {Input}", identityInput);
        return null;
    }

    private IdentityType DetermineServicePrincipalType(string? servicePrincipalType, IEnumerable<string>? alternativeNames)
    {
        const string SystemAssignedIndicator = "isExplicit=False";
        const string UserAssignedIndicator = "isExplicit=True";

        if (servicePrincipalType != "ManagedIdentity")
        {
            // For non-managed identity service principals, treat them all as regular service principals
            return IdentityType.ServicePrincipal;
        }

        // For Managed Identities, check alternativeNames to distinguish System vs User Assigned
        // System-Assigned MI: alternativeNames contains "isExplicit=False"
        // User-Assigned MI: alternativeNames contains "isExplicit=True"
        if (alternativeNames != null)
        {
            var altNamesList = alternativeNames.ToList();
            foreach (var altName in altNamesList)
            {
                if (altName.Contains(SystemAssignedIndicator, StringComparison.OrdinalIgnoreCase))
                {
                    return IdentityType.SystemAssignedManagedIdentity;
                }
                else if (altName.Contains(UserAssignedIndicator, StringComparison.OrdinalIgnoreCase))
                {
                    return IdentityType.UserAssignedManagedIdentity;
                }
            }

            // If we couldn't determine the type, log the alternativeNames for debugging
            _logger.LogWarning("Unable to determine managed identity type from {Count} alternativeNames, defaulting to User-Assigned. AlternativeNames: {AltNames}",
                altNamesList.Count, string.Join("; ", altNamesList));
        }
        else
        {
            _logger.LogWarning("Unable to determine managed identity type - no alternativeNames provided, defaulting to User-Assigned");
        }

        // Default to User-Assigned if we can't determine (more common case)
        return IdentityType.UserAssignedManagedIdentity;
    }

    private void ExtractManagedIdentityResourceInfo(IEnumerable<string> alternativeNames, SharedIdentity identity)
    {
        // alternativeNames for managed identities contain resource information in the format:
        // Example: "/subscriptions/{subscriptionId}/resourcegroups/{resourceGroup}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}"
        // or for system-assigned: "/subscriptions/{subscriptionId}/resourcegroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachines/{vmName}"
        
        foreach (var altName in alternativeNames)
        {
            if (altName.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var parts = altName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Equals("subscriptions", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            identity.SubscriptionId = parts[i + 1];
                        }
                        else if (parts[i].Equals("resourcegroups", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            identity.ResourceGroup = parts[i + 1];
                        }
                    }

                    // If we found subscription ID, we've extracted what we need from this alternativeName
                    // (resource group may or may not be present in the same path)
                    if (!string.IsNullOrEmpty(identity.SubscriptionId))
                    {
                        _logger.LogInformation("Extracted MI resource info - SubscriptionId: {SubId}, ResourceGroup: {RG}", 
                            identity.SubscriptionId, identity.ResourceGroup ?? "N/A");
                        break; // Stop processing additional alternativeNames
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing alternativeName: {AltName}", altName);
                }
            }
        }
    }

    private async Task<string?> GetAppRegistrationObjectIdAsync(string? appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return null;
        }

        try
        {
            var escapedAppId = EscapeODataFilterValue(appId);
            var apps = await _graphClient.Applications.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"appId eq '{escapedAppId}'";
                config.QueryParameters.Top = 1;
            });

            var app = apps?.Value?.FirstOrDefault();
            if (app != null)
            {
                _logger.LogInformation("Found App Registration with ObjectId: {ObjectId} for AppId: {AppId}", app.Id, appId);
                return app.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch App Registration for AppId: {AppId}", appId);
        }

        return null;
    }

    private static string EscapeODataFilterValue(string value)
    {
        // Escape single quotes to prevent OData filter injection
        return value.Replace("'", "''");
    }

    private async Task<SharedIdentity> CreateIdentityFromServicePrincipalAsync(ServicePrincipal sp)
    {
        var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType, sp.AlternativeNames);
        var appRegistrationId = await GetAppRegistrationObjectIdAsync(sp.AppId);
        var identity = new SharedIdentity
        {
            ObjectId = sp.Id ?? "",
            ApplicationId = sp.AppId ?? "",
            Email = "",
            Name = sp.DisplayName ?? "",
            Type = identityType,
            ServicePrincipalType = sp.ServicePrincipalType,
            AppRegistrationId = appRegistrationId
        };
        
        // For Managed Identities, extract resource information from alternativeNames
        if (sp.ServicePrincipalType == "ManagedIdentity" && sp.AlternativeNames != null)
        {
            ExtractManagedIdentityResourceInfo(sp.AlternativeNames, identity);
        }
        
        return identity;
    }
}
