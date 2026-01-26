using Matriarch.Web.Models;
using Microsoft.Extensions.Logging;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for managing subscription data with in-memory caching.
/// Fetches all subscriptions on first access and caches them for the application lifetime.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IResourceGraphService _resourceGraphService;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
    private Dictionary<string, SubscriptionDto> _subscriptionCache = new();
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
            _logger.LogInformation("Refreshing subscription cache...");
            var subscriptions = await _resourceGraphService.FetchAllSubscriptionsAsync();

            // Fetch management group hierarchy for each subscription in parallel
            var tasks = subscriptions.Select(async sub =>
            {
                try
                {
                    var hierarchy = await _resourceGraphService.FetchManagementGroupHierarchyAsync(sub.SubscriptionId);
                    sub.ManagementGroupHierarchy = hierarchy;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching management group hierarchy for subscription {SubscriptionId}", sub.SubscriptionId);
                    sub.ManagementGroupHierarchy = new List<string>();
                }
            });

            await Task.WhenAll(tasks);

            // Update cache
            _subscriptionCache = subscriptions.ToDictionary(s => s.SubscriptionId, StringComparer.OrdinalIgnoreCase);
            _cacheInitialized = true;

            _logger.LogInformation("Subscription cache refreshed with {Count} subscriptions", subscriptions.Count);
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
