using Matriarch.Models;

namespace Matriarch.Services;

public interface INeo4jService : IAsyncDisposable
{
    Task InitializeDatabaseAsync();
    Task StoreAppRegistrationsAsync(List<AppRegistrationDto> appRegistrations);
    Task StoreEnterpriseApplicationsAsync(List<EnterpriseApplicationDto> enterpriseApps);
    Task StoreSecurityGroupsAsync(List<SecurityGroupDto> securityGroups);
    Task StoreRoleAssignmentsAsync(List<RoleAssignmentDto> roleAssignments, List<EnterpriseApplicationDto> enterpriseApps, List<SecurityGroupDto> securityGroups);
    Task StoreGroupMembershipsAsync(List<EnterpriseApplicationDto> enterpriseApps);
    Task StoreSecurityGroupMembersAsync(List<SecurityGroupDto> securityGroups);
}
