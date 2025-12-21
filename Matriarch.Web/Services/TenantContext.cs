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
    private readonly List<string> _availableTenants;
    private readonly object _lock = new object();
    private string? _currentTenant;

    public TenantContext(AppSettings appSettings)
    {
        _appSettings = appSettings;
        _availableTenants = _appSettings.Azure.Keys.ToList();
        
        // Set default tenant to the first available tenant
        if (_availableTenants.Any())
        {
            _currentTenant = _availableTenants.First();
        }
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

    public List<string> GetAvailableTenants()
    {
        return _availableTenants;
    }
}
