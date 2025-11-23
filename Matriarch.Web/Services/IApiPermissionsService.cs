using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public interface IApiPermissionsService
{
    /// <summary>
    /// Gets API permissions (app role assignments) for an identity.
    /// </summary>
    /// <param name="identity">The identity to fetch API permissions for</param>
    /// <returns>
    /// List of API permissions for service principals and managed identities.
    /// Returns an empty list for users and groups (as they don't have API permissions).
    /// </returns>
    Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity);
}
