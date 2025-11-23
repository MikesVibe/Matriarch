using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public class MockApiPermissionsService : IApiPermissionsService
{
    public Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity)
    {
        // Users and Groups don't have API permissions
        if (identity.Type == IdentityType.User || identity.Type == IdentityType.Group)
        {
            return Task.FromResult(new List<ApiPermission>());
        }

        // Mock API permissions for service principals and managed identities
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
