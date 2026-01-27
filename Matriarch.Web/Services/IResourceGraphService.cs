using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

/// <summary>
/// Service for querying Azure Resource Graph API to fetch role assignments.
/// </summary>
public interface IResourceGraphService
{
    /// <summary>
    /// Fetches role assignments for specific principal IDs from Azure Resource Graph.
    /// </summary>
    /// <param name="principalIds">List of principal IDs (user, service principal, or group object IDs)</param>
    /// <returns>List of role assignments for the specified principals</returns>
    Task<List<AzureRoleAssignmentDto>> FetchRoleAssignmentsForPrincipalsAsync(List<string> principalIds);

    /// <summary>
    /// Fetches Key Vault access policies for specific principal IDs from Azure Resource Graph.
    /// </summary>
    /// <param name="principalIds">List of principal IDs (user, service principal, or group object IDs)</param>
    /// <returns>List of Key Vaults with their access policies filtered for the specified principals</returns>
    Task<List<KeyVaultDto>> FetchKeyVaultAccessPoliciesForPrincipalsAsync(List<string> principalIds);

    /// <summary>
    /// Fetches managed identities for resources with a specific tag from Azure Resource Graph.
    /// </summary>
    /// <param name="tagValue">The tag value to search for (e.g., 'ptci-1567')</param>
    /// <returns>List of managed identity resources with the specified tag</returns>
    Task<List<ManagedIdentityResourceDto>> FetchManagedIdentitiesByTagAsync(string tagValue);

    /// <summary>
    /// Fetches all subscriptions accessible to the current credentials from Azure Resource Graph.
    /// </summary>
    /// <returns>List of subscriptions</returns>
    Task<List<SubscriptionDto>> FetchAllSubscriptionsAsync();

    /// <summary>
    /// Fetches all management groups accessible to the current credentials from Azure Resource Graph.
    /// </summary>
    /// <returns>List of management groups with their display names</returns>
    Task<List<ManagementGroupDto>> FetchAllManagementGroupsAsync();

    /// <summary>
    /// Fetches the management group hierarchy for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <returns>List of management group names from root to child</returns>
    Task<List<string>> FetchManagementGroupHierarchyAsync(string subscriptionId);
}
