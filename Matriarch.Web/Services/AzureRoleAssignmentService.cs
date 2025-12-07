using Matriarch.Web.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Matriarch.Web.Services;

public class AzureRoleAssignmentService : IRoleAssignmentService
{
    private readonly ILogger<AzureRoleAssignmentService> _logger;
    private readonly IIdentityService _identityService;
    private readonly IGroupManagementService _groupManagementService;
    private readonly IApiPermissionsService _apiPermissionsService;
    private readonly IResourceGraphService _resourceGraphService;

    public AzureRoleAssignmentService(
        ILogger<AzureRoleAssignmentService> logger,
        IIdentityService identityService,
        IGroupManagementService groupManagementService,
        IApiPermissionsService apiPermissionsService,
        IResourceGraphService resourceGraphService)
    {
        _logger = logger;
        _identityService = identityService;
        _groupManagementService = groupManagementService;
        _apiPermissionsService = apiPermissionsService;
        _resourceGraphService = resourceGraphService;
    }

    public async Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(Identity identity)
    {
        var (result, _) = await GetRoleAssignmentsAsync(identity, false);
        return result;
    }

    public async Task<(IdentityRoleAssignmentResult result, TimeSpan elapsedTime)> GetRoleAssignmentsAsync(Identity identity, bool useParallelProcessing)
    {
        _logger.LogInformation("Fetching role assignments from Azure for identity: {Name} ({ObjectId})", identity.Name, identity.ObjectId);

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Step 1: Get direct group memberships
            var directGroups = await _groupManagementService.GetGroupMembershipsAsync(identity);
            var parentGroups = await _groupManagementService.GetTransitiveGroupsAsync(directGroups);


            _logger.LogInformation("Found {DirectCount} direct groups and {TotalCount} total groups (including indirect)",
                directGroups.Count, parentGroups.Count);

            // Step 2: Fetch role assignments for principal and ALL groups (direct and indirect)
            var principalIds = new List<string> { identity.ObjectId };
            principalIds.AddRange(directGroups.Select(x => x.Id));
            principalIds.AddRange(parentGroups.Select(x => x.Id));

            var roleAssignments = await _resourceGraphService.FetchRoleAssignmentsForPrincipalsAsync(principalIds);

            // Filter direct role assignments (only for the user/service principal)
            var directRoleAssignments = roleAssignments
                .Where(ra => ra.PrincipalId == identity.ObjectId)
                .Select(ra => new RoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = "Direct Assignment"
                })
                .ToList();

            // Step 3: Build security group hierarchy with role assignments using pre-fetched group info
            var securityDirectGroups = _groupManagementService.BuildSecurityGroupsWithPreFetchedData(directGroupIds, groupInfoMap, roleAssignments);
            var securityIndirectGroups = _groupManagementService.BuildSecurityGroupsWithPreFetchedData(parentGroupIds, groupInfoMap, roleAssignments);

            // Step 4: Fetch API permissions (only for service principals and managed identities)
            var apiPermissions = await _apiPermissionsService.GetApiPermissionsAsync(identity);

            totalStopwatch.Stop();

            var result = new IdentityRoleAssignmentResult
            {
                Identity = identity,
                //DirectRoleAssignments = directRoleAssignments,
                SecurityDirectGroups = directGroups,
                SecurityIndirectGroups = parentGroups,
                //ApiPermissions = apiPermissions
            };

            return (result, totalStopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Azure");
            throw;
        }
    }

    public async Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput)
    {
        return await _identityService.SearchIdentitiesAsync(searchInput);
    }
}
