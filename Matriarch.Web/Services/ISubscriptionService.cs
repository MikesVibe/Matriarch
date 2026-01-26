using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for managing subscription data and caching.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Gets subscription information by subscription ID. Uses cache and auto-refreshes if not found.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <returns>Subscription information or null if not found</returns>
    Task<SubscriptionDto?> GetSubscriptionAsync(string subscriptionId);

    /// <summary>
    /// Refreshes the subscription cache by fetching all subscriptions from Azure Resource Graph.
    /// </summary>
    Task RefreshSubscriptionCacheAsync();

    /// <summary>
    /// Gets all cached subscriptions.
    /// </summary>
    /// <returns>List of all subscriptions</returns>
    Task<List<SubscriptionDto>> GetAllSubscriptionsAsync();
}
