using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public interface IApiPermissionsService
{
    /// <summary>
    /// Gets API permissions (app role assignments) for a service principal.
    /// </summary>
    /// <param name="principalId">The Object ID of the service principal</param>
    /// <returns>List of API permissions, or empty list if the identity is not a service principal</returns>
    Task<List<ApiPermission>> GetApiPermissionsAsync(string principalId);
}
