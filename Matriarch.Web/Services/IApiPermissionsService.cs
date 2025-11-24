using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public interface IApiPermissionsService
{
    /// <summary>
    /// Gets API permissions for an identity.
    /// </summary>
    /// <param name="identity">The identity to fetch API permissions for</param>
    /// <returns>
    /// List of API permissions:
    /// - For service principals and managed identities: Application permissions (app role assignments)
    /// - For users: Delegated permissions (OAuth2 grants) and user role assignments
    /// - For groups: Returns an empty list (groups don't have API permissions)
    /// </returns>
    Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity);
}
