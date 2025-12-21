using Matriarch.Web.Configuration;

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
    List<string> GetAvailableTenants();
}

public class TenantContext : ITenantContext
{
    private readonly AppSettings _appSettings;
    private string? _currentTenant;

    public TenantContext(AppSettings appSettings)
    {
        _appSettings = appSettings;
        
        // Set default tenant to the first available tenant
        if (_appSettings.Azure.Any())
        {
            _currentTenant = _appSettings.Azure.Keys.First();
        }
    }

    public string? CurrentTenant => _currentTenant;

    public AzureSettings GetCurrentTenantSettings()
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

    public void SetCurrentTenant(string tenantName)
    {
        if (!_appSettings.Azure.ContainsKey(tenantName))
        {
            throw new ArgumentException($"Tenant '{tenantName}' not found in configuration", nameof(tenantName));
        }

        _currentTenant = tenantName;
    }

    public List<string> GetAvailableTenants()
    {
        return _appSettings.Azure.Keys.ToList();
    }
}
