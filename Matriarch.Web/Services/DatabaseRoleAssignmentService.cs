using Matriarch.Data;
using Matriarch.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Matriarch.Web.Services;

public class DatabaseRoleAssignmentService : IRoleAssignmentService
{
    private readonly MatriarchDbContext _dbContext;

    public DatabaseRoleAssignmentService(MatriarchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(string objectId, string applicationId, string email, string name)
    {
        var identity = new Identity
        {
            ObjectId = objectId,
            ApplicationId = applicationId,
            Email = email,
            Name = name
        };

        // Get direct role assignments for this principal (by objectId or applicationId)
        var directRoleAssignments = await _dbContext.RoleAssignments
            .Where(ra => ra.PrincipalId == objectId || ra.PrincipalId == applicationId)
            .Select(ra => new RoleAssignment
            {
                Id = ra.Id,
                RoleName = ra.RoleName,
                Scope = ra.Scope,
                AssignedTo = "Direct Assignment"
            })
            .ToListAsync();

        // Get security groups this identity is a member of (directly)
        var directGroupIds = await _dbContext.GroupMemberships
            .Where(gm => gm.MemberId == objectId || gm.MemberId == applicationId)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        // Build security groups with role assignments and handle circular dependencies
        var securityGroups = new List<SecurityGroup>();
        var processedGroups = new HashSet<string>(); // To prevent infinite loops

        foreach (var groupId in directGroupIds)
        {
            var group = await BuildSecurityGroupWithParentsAsync(groupId, processedGroups);
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return new IdentityRoleAssignmentResult
        {
            Identity = identity,
            DirectRoleAssignments = directRoleAssignments,
            SecurityGroups = securityGroups
        };
    }

    private async Task<SecurityGroup?> BuildSecurityGroupWithParentsAsync(string groupId, HashSet<string> processedGroups)
    {
        // Prevent infinite loops - if we've already processed this group, return null
        if (processedGroups.Contains(groupId))
        {
            return null;
        }

        // Mark this group as processed
        processedGroups.Add(groupId);

        // Get the group entity
        var groupEntity = await _dbContext.SecurityGroups
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (groupEntity == null)
        {
            return null;
        }

        // Get role assignments for this group
        var roleAssignments = await _dbContext.RoleAssignments
            .Where(ra => ra.PrincipalId == groupId)
            .Select(ra => new RoleAssignment
            {
                Id = ra.Id,
                RoleName = ra.RoleName,
                Scope = ra.Scope,
                AssignedTo = groupEntity.DisplayName
            })
            .ToListAsync();

        // Get parent groups (groups that this group is a member of)
        var parentGroupIds = await _dbContext.GroupMemberships
            .Where(gm => gm.MemberId == groupId && gm.MemberType == "SecurityGroup")
            .Select(gm => gm.GroupId)
            .ToListAsync();

        // Recursively build parent groups, but skip any that are already processed
        var parentGroups = new List<SecurityGroup>();
        foreach (var parentGroupId in parentGroupIds)
        {
            // Create a copy of processedGroups for each parent to handle diamond dependencies correctly
            var parentProcessedGroups = new HashSet<string>(processedGroups);
            var parentGroup = await BuildSecurityGroupWithParentsAsync(parentGroupId, parentProcessedGroups);
            if (parentGroup != null)
            {
                parentGroups.Add(parentGroup);
            }
        }

        return new SecurityGroup
        {
            Id = groupEntity.Id,
            DisplayName = groupEntity.DisplayName,
            Description = groupEntity.Description ?? string.Empty,
            RoleAssignments = roleAssignments,
            ParentGroups = parentGroups
        };
    }
}
