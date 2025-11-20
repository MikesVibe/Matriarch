using Matriarch.Models;

namespace Matriarch.Services;

public interface IAzureDataService
{
    Task<List<RoleAssignmentDto>> FetchRoleAssignmentsAsync();
    Task<List<EnterpriseApplicationDto>> FetchEnterpriseApplicationsAsync();
    Task<List<AppRegistrationDto>> FetchAppRegistrationsAsync();
    Task<List<SecurityGroupDto>> FetchSecurityGroupsAsync();
    Task FetchMembersForSecurityGroupsAsync(List<SecurityGroupDto> groups);
    Task FetchGroupMembershipsForLinkedAppsAsync(List<EnterpriseApplicationDto> apps);
}
