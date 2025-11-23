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
                    var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType);
                    var appRegistrationId = await GetAppRegistrationObjectIdAsync(sp.AppId);
                    identities.Add(new SharedIdentity
                    {
                        ObjectId = sp.Id ?? "",
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? "",
                        Type = identityType,
                        ServicePrincipalType = sp.ServicePrincipalType,
                        AppRegistrationId = appRegistrationId
                    });
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
            // It's a GUID - could be ObjectId, ApplicationId, or GroupId
            // Try as User first
            try
            {
                var user = await _graphClient.Users[identityInput].GetAsync();
                if (user != null)
                {
                    return new SharedIdentity
                    {
                        ObjectId = user.Id ?? identityInput,
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? "",
                        Type = IdentityType.User
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as user, trying as service principal");
            }

            // Try as Service Principal (by ObjectId)
            try
            {
                var sp = await _graphClient.ServicePrincipals[identityInput].GetAsync();
                if (sp != null)
                {
                    var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType);
                    var appRegistrationId = await GetAppRegistrationObjectIdAsync(sp.AppId);
                    return new SharedIdentity
                    {
                        ObjectId = sp.Id ?? identityInput,
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? "",
                        Type = identityType,
                        ServicePrincipalType = sp.ServicePrincipalType,
                        AppRegistrationId = appRegistrationId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as service principal by ObjectId, trying as ApplicationId");
            }

            // Try as Application ID (find the App Registration and its Enterprise Application)
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
                    var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType);
                    var appRegistrationId = await GetAppRegistrationObjectIdAsync(sp.AppId);
                    return new SharedIdentity
                    {
                        ObjectId = sp.Id ?? "",
                        ApplicationId = sp.AppId ?? identityInput,
                        Email = "",
                        Name = sp.DisplayName ?? "",
                        Type = identityType,
                        ServicePrincipalType = sp.ServicePrincipalType,
                        AppRegistrationId = appRegistrationId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as application by ApplicationId, trying as App Registration by ObjectId");
            }

            // Try as App Registration (by ObjectId)
            try
            {
                var app = await _graphClient.Applications[identityInput].GetAsync();
                if (app != null && !string.IsNullOrEmpty(app.AppId))
                {
                    _logger.LogInformation("Found App Registration by ObjectId: {ObjectId}, looking for Enterprise Application", identityInput);
                    
                    // Find the corresponding Service Principal (Enterprise Application) using the AppId
                    var escapedAppId = EscapeODataFilterValue(app.AppId);
                    var sps = await _graphClient.ServicePrincipals.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = $"appId eq '{escapedAppId}'";
                        config.QueryParameters.Top = 1;
                    });

                    var sp = sps?.Value?.FirstOrDefault();
                    if (sp != null)
                    {
                        var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType);
                        return new SharedIdentity
                        {
                            ObjectId = sp.Id ?? "",
                            ApplicationId = sp.AppId ?? app.AppId,
                            Email = "",
                            Name = sp.DisplayName ?? app.DisplayName ?? "",
                            Type = identityType,
                            ServicePrincipalType = sp.ServicePrincipalType,
                            AppRegistrationId = app.Id // Use the App Registration ObjectId we already have
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Found App Registration (ObjectId: {ObjectId}, AppId: {AppId}) but no corresponding Enterprise Application exists in this tenant", app.Id, app.AppId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Not found as App Registration by ObjectId, trying as group");
            }

            // Try as Group (by ObjectId)
            try
            {
                var group = await _graphClient.Groups[identityInput].GetAsync();
                if (group != null && group.SecurityEnabled == true)
                {
                    _logger.LogInformation("Found Security Group by ID: {GroupId}", identityInput);
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Identity not found by GUID");
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
                    var identityType = DetermineServicePrincipalType(sp.ServicePrincipalType);
                    var appRegistrationId = await GetAppRegistrationObjectIdAsync(sp.AppId);
                    return new SharedIdentity
                    {
                        ObjectId = sp.Id ?? "",
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? identityInput,
                        Type = identityType,
                        ServicePrincipalType = sp.ServicePrincipalType,
                        AppRegistrationId = appRegistrationId
                    };
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

    private static IdentityType DetermineServicePrincipalType(string? servicePrincipalType)
    {
        return servicePrincipalType switch
        {
            "ManagedIdentity" => IdentityType.UserAssignedManagedIdentity,
            "Application" => IdentityType.ServicePrincipal,
            _ => IdentityType.ServicePrincipal
        };
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
}
