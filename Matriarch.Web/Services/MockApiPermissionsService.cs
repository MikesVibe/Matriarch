using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public class MockApiPermissionsService : IApiPermissionsService
{
    public Task<List<ApiPermission>> GetApiPermissionsAsync(string principalId)
    {
        // Mock API permissions
        var apiPermissions = new List<ApiPermission>
        {
            new ApiPermission
            {
                Id = "api-1",
                ResourceDisplayName = "Microsoft Graph",
                ResourceId = "00000003-0000-0000-c000-000000000000",
                PermissionType = "Application",
                PermissionValue = "User.Read.All"
            },
            new ApiPermission
            {
                Id = "api-2",
                ResourceDisplayName = "Microsoft Graph",
                ResourceId = "00000003-0000-0000-c000-000000000000",
                PermissionType = "Application",
                PermissionValue = "Directory.Read.All"
            }
        };

        return Task.FromResult(apiPermissions);
    }
}
