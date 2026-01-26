using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for managing subscription and management group data and caching.
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
    /// Gets management group information by management group name. Uses cache and auto-refreshes if not found.
    /// </summary>
    /// <param name="managementGroupName">The management group name</param>
    /// <returns>Management group information or null if not found</returns>
    Task<ManagementGroupDto?> GetManagementGroupAsync(string managementGroupName);

    /// <summary>
    /// Refreshes the subscription and management group cache by fetching all data from Azure Resource Graph.
    /// </summary>
    Task RefreshSubscriptionCacheAsync();

    /// <summary>
    /// Gets all cached subscriptions.
    /// </summary>
    /// <returns>List of all subscriptions</returns>
    Task<List<SubscriptionDto>> GetAllSubscriptionsAsync();
}
