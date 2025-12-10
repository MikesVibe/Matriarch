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

            // Step 2: Fetch role assignments for the identity
            var identityRoleAssignments = await _resourceGraphService.FetchRoleAssignmentsForPrincipalsAsync(
                new List<string> { identity.ObjectId });

            var directRoleAssignments = identityRoleAssignments
                .Select(ra => new RoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = "Direct Assignment"
                })
                .ToList();

            // Step 3: Fetch role assignments for groups separately (only if there are groups)
            var groupIds = directGroups.Select(x => x.Id)
                .Concat(parentGroups.Select(x => x.Id))
                .Distinct()
                .ToList();

            if (groupIds.Count > 0)
            {
                var groupRoleAssignments = await _resourceGraphService.FetchRoleAssignmentsForPrincipalsAsync(groupIds);

                // Step 4: Add role assignments to security groups
                AddRoleAssignmentsToGroups(directGroups, groupRoleAssignments);
                AddRoleAssignmentsToGroups(parentGroups, groupRoleAssignments);
            }

            // Step 5: Fetch API permissions (only for service principals and managed identities)
            var apiPermissions = await _apiPermissionsService.GetApiPermissionsAsync(identity);

            totalStopwatch.Stop();

            var result = new IdentityRoleAssignmentResult
            {
                Identity = identity,
                DirectRoleAssignments = directRoleAssignments,
                SecurityDirectGroups = directGroups,
                SecurityIndirectGroups = parentGroups,
                ApiPermissions = apiPermissions
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

    public async Task<List<RoleAssignment>> GetDirectRoleAssignmentsAsync(Identity identity)
    {
        _logger.LogInformation("Fetching direct role assignments for identity: {Name} ({ObjectId})", identity.Name, identity.ObjectId);
        
        var identityRoleAssignments = await _resourceGraphService.FetchRoleAssignmentsForPrincipalsAsync(
            new List<string> { identity.ObjectId });

        return identityRoleAssignments
            .Select(ra => new RoleAssignment
            {
                Id = ra.Id,
                RoleName = ra.RoleName,
                Scope = ra.Scope,
                AssignedTo = "Direct Assignment"
            })
            .ToList();
    }

    public async Task<List<SecurityGroup>> GetDirectGroupsAsync(Identity identity)
    {
        _logger.LogInformation("Fetching direct groups for identity: {Name} ({ObjectId})", identity.Name, identity.ObjectId);
        
        return await _groupManagementService.GetGroupMembershipsAsync(identity);
    }

    public async Task<List<SecurityGroup>> GetIndirectGroupsAsync(List<SecurityGroup> directGroups, bool useParallelProcessing = false)
    {
        _logger.LogInformation("Fetching indirect groups for {Count} direct groups (parallel: {UseParallel})", 
            directGroups.Count, useParallelProcessing);
        
        return await _groupManagementService.GetTransitiveGroupsAsync(directGroups, useParallelProcessing);
    }

    public async Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity)
    {
        _logger.LogInformation("Fetching API permissions for identity: {Name} ({ObjectId})", identity.Name, identity.ObjectId);
        
        return await _apiPermissionsService.GetApiPermissionsAsync(identity);
    }

    public async Task PopulateGroupRoleAssignmentsAsync(List<SecurityGroup> directGroups, List<SecurityGroup> indirectGroups)
    {
        _logger.LogInformation("Populating role assignments for {DirectCount} direct and {IndirectCount} indirect groups", 
            directGroups.Count, indirectGroups.Count);

        var groupIds = directGroups.Select(x => x.Id)
            .Concat(indirectGroups.Select(x => x.Id))
            .Distinct()
            .ToList();

        if (groupIds.Count > 0)
        {
            var groupRoleAssignments = await _resourceGraphService.FetchRoleAssignmentsForPrincipalsAsync(groupIds);
            
            AddRoleAssignmentsToGroups(directGroups, groupRoleAssignments);
            AddRoleAssignmentsToGroups(indirectGroups, groupRoleAssignments);
        }
    }

    private void AddRoleAssignmentsToGroups(List<SecurityGroup> groups, List<AzureRoleAssignmentDto> roleAssignments)
    {
        foreach (var group in groups)
        {
            group.RoleAssignments = roleAssignments
                .Where(ra => ra.PrincipalId == group.Id)
                .Select(ra => new RoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = group.DisplayName
                })
                .ToList();
        }
    }
}
