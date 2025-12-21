using Matriarch.Web.Configuration;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Matriarch.Web.Services;

/// <summary>
/// Service to manage the currently selected Azure tenant context.
/// This is a scoped service that maintains state per-user session.
/// </summary>
public interface ITenantContext
{
    string? CurrentTenant { get; }
    AzureSettings GetCurrentTenantSettings();
    void SetCurrentTenant(string tenantName);
    Task<List<string>> GetAvailableTenantsAsync();
}

public class TenantContext : ITenantContext
{
    private readonly AppSettings _appSettings;
    private readonly ITenantAccessService _tenantAccessService;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly object _lock = new object();
    private string? _currentTenant;
    private List<string>? _cachedAvailableTenants;

    public TenantContext(
        AppSettings appSettings, 
        ITenantAccessService tenantAccessService,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _appSettings = appSettings;
        _tenantAccessService = tenantAccessService;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public string? CurrentTenant
    {
        get
        {
            lock (_lock)
            {
                return _currentTenant;
            }
        }
    }

    public AzureSettings GetCurrentTenantSettings()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_currentTenant))
            {
                throw new InvalidOperationException("No tenant selected. Please configure Azure tenants in appsettings.json");
            }

            if (!_appSettings.Azure.TryGetValue(_currentTenant, out var settings))
            {
                throw new InvalidOperationException($"Tenant '{_currentTenant}' not found in configuration");
            }

            return settings;
        }
    }

    public void SetCurrentTenant(string tenantName)
    {
        lock (_lock)
        {
            if (!_appSettings.Azure.ContainsKey(tenantName))
            {
                throw new ArgumentException($"Tenant '{tenantName}' not found in configuration", nameof(tenantName));
            }

            _currentTenant = tenantName;
        }
    }

    public async Task<List<string>> GetAvailableTenantsAsync()
    {
        // Return cached tenants if available
        if (_cachedAvailableTenants != null)
        {
            return _cachedAvailableTenants;
        }

        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userPrincipalName = user.FindFirst(ClaimTypes.Name)?.Value 
                                  ?? user.FindFirst(ClaimTypes.Upn)?.Value
                                  ?? user.FindFirst("preferred_username")?.Value;

            if (!string.IsNullOrEmpty(userPrincipalName))
            {
                _cachedAvailableTenants = await _tenantAccessService.GetAccessibleTenantsAsync(userPrincipalName);
                
                // Set default tenant to the first available tenant
                lock (_lock)
                {
                    if (_cachedAvailableTenants.Any() && string.IsNullOrEmpty(_currentTenant))
                    {
                        _currentTenant = _cachedAvailableTenants.First();
                    }
                }
                
                return _cachedAvailableTenants;
            }
        }

        // If user is not authenticated or we can't get their info, return all tenants
        _cachedAvailableTenants = _appSettings.Azure.Keys.ToList();
        
        // Set default tenant to the first available tenant
        lock (_lock)
        {
            if (_cachedAvailableTenants.Any() && string.IsNullOrEmpty(_currentTenant))
            {
                _currentTenant = _cachedAvailableTenants.First();
            }
        }
        
        return _cachedAvailableTenants;
    }
}
