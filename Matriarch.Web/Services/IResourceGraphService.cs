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
}
