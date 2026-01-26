using Matriarch.Web.Models;
using Microsoft.Extensions.Logging;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for managing subscription and management group data with in-memory caching.
/// Fetches all subscriptions and management groups on first access and caches them for the application lifetime.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
    private Dictionary<string, SubscriptionDto> _subscriptionCache = new();
    private Dictionary<string, ManagementGroupDto> _managementGroupCache = new();
    private bool _cacheInitialized = false;

    public SubscriptionService(
        IResourceGraphService resourceGraphService,
        ILogger<SubscriptionService> logger)
    {
        _resourceGraphService = resourceGraphService;
        _logger = logger;
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return null;
        }

        // Ensure cache is initialized
        await EnsureCacheInitializedAsync();

        // Try to get from cache
        if (_subscriptionCache.TryGetValue(subscriptionId, out var subscription))
        {
            return subscription;
        }

        // Not found - refresh cache and try again
        _logger.LogInformation("Subscription {SubscriptionId} not found in cache, refreshing...", subscriptionId);
        await RefreshSubscriptionCacheAsync();

        // Try again after refresh
        if (_subscriptionCache.TryGetValue(subscriptionId, out subscription))
        {
            return subscription;
        }

        _logger.LogWarning("Subscription {SubscriptionId} not found even after cache refresh", subscriptionId);
        return null;
    }

    public async Task<ManagementGroupDto?> GetManagementGroupAsync(string managementGroupName)
    {
        if (string.IsNullOrWhiteSpace(managementGroupName))
        {
            return null;
        }

        // Ensure cache is initialized
        await EnsureCacheInitializedAsync();

        // Try to get from cache
        if (_managementGroupCache.TryGetValue(managementGroupName, out var mg))
        {
            return mg;
        }

        // Not found - refresh cache and try again
        _logger.LogInformation("Management group {MgName} not found in cache, refreshing...", managementGroupName);
        await RefreshSubscriptionCacheAsync();

        // Try again after refresh
        if (_managementGroupCache.TryGetValue(managementGroupName, out mg))
        {
            return mg;
        }

        _logger.LogWarning("Management group {MgName} not found even after cache refresh", managementGroupName);
        return null;
    }

    public async Task<List<SubscriptionDto>> GetAllSubscriptionsAsync()
    {
        await EnsureCacheInitializedAsync();
        return _subscriptionCache.Values.ToList();
    }

    public async Task RefreshSubscriptionCacheAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            _logger.LogInformation("Refreshing subscription and management group cache...");
            
            // Fetch all subscriptions with management groups in a single query
            var subscriptions = await _resourceGraphService.FetchAllSubscriptionsAsync();

            // Fetch all management groups
            var managementGroups = await _resourceGraphService.FetchAllManagementGroupsAsync();

            // Update caches
            _subscriptionCache = subscriptions.ToDictionary(s => s.SubscriptionId, StringComparer.OrdinalIgnoreCase);
            _managementGroupCache = managementGroups.ToDictionary(mg => mg.Name, StringComparer.OrdinalIgnoreCase);
            _cacheInitialized = true;

            _logger.LogInformation("Cache refreshed with {SubCount} subscriptions and {MgCount} management groups", 
                subscriptions.Count, managementGroups.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task EnsureCacheInitializedAsync()
    {
        if (_cacheInitialized)
        {
            return;
        }

        await RefreshSubscriptionCacheAsync();
    }
}
