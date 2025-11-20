using Matriarch.Models;
using Matriarch.Shared.Models;

namespace Matriarch.Services;

public interface ISqliteService
{
    Task InitializeDatabaseAsync();
    Task StoreDataAsync(
        List<AppRegistrationDto> appRegistrations,
        List<EnterpriseApplicationDto> enterpriseApps,
        List<SecurityGroupDto> securityGroups,
        List<RoleAssignmentDto> roleAssignments);
    Task<IdentityRoleAssignmentResult?> GetIdentityRoleAssignmentsAsync(string objectId);
}
