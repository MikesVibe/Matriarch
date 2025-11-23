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

    public async Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(string identityInput)
    {
        // Try to find the identity in the database
        // Search by ObjectId (GUID), Email, or Name
        string? objectId = null;
        string? applicationId = null;
        string? email = null;
        string? name = null;

        // Check if input is a GUID
        if (Guid.TryParse(identityInput, out _))
        {
            objectId = identityInput;
            // TODO: Query identity table to find additional info (email, name) by objectId
        }
        else if (identityInput.Contains("@"))
        {
            email = identityInput;
            // TODO: Query identity table to find objectId from email
        }
        else
        {
            name = identityInput;
            // TODO: Query identity table to find objectId from name
        }

        var identity = new Identity
        {
            ObjectId = objectId ?? string.Empty,
            ApplicationId = applicationId ?? string.Empty,
            Email = email ?? string.Empty,
            Name = name ?? string.Empty,
            Type = IdentityType.User // Default to User for database queries
        };

        // Only query database if we have a valid objectId (GUID)
        // PrincipalId and MemberId fields in the database are GUIDs
        var directRoleAssignments = new List<RoleAssignment>();
        var directGroupIds = new List<string>();

        if (!string.IsNullOrEmpty(objectId))
        {
            // Get direct role assignments for this principal
            directRoleAssignments = await _dbContext.RoleAssignments
                .Where(ra => ra.PrincipalId == objectId)
                .Select(ra => new RoleAssignment
                {
                    Id = ra.Id,
                    RoleName = ra.RoleName,
                    Scope = ra.Scope,
                    AssignedTo = "Direct Assignment"
                })
                .ToListAsync();

            // Get security groups this identity is a member of (directly)
            directGroupIds = await _dbContext.GroupMemberships
                .Where(gm => gm.MemberId == objectId)
                .Select(gm => gm.GroupId)
                .ToListAsync();
        }

        // Build security groups with role assignments and handle circular dependencies
        // Use a shared set to track all processed groups across the entire query
        var securityGroups = new List<SecurityGroup>();
        var allProcessedGroups = new HashSet<string>(); // Global tracking to ensure each group appears once

        foreach (var groupId in directGroupIds)
        {
            var group = await BuildSecurityGroupWithParentsAsync(groupId, allProcessedGroups, new HashSet<string>());
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return new IdentityRoleAssignmentResult
        {
            Identity = identity,
            DirectRoleAssignments = directRoleAssignments,
            SecurityGroups = securityGroups,
            ApiPermissions = new List<ApiPermission>() // Not stored in database yet
        };
    }

    private async Task<SecurityGroup?> BuildSecurityGroupWithParentsAsync(
        string groupId, 
        HashSet<string> allProcessedGroups,
        HashSet<string> currentPath)
    {
        // Prevent infinite loops - if this group is in the current path, we have a circular reference
        if (currentPath.Contains(groupId))
        {
            return null;
        }

        // If we've already fully processed this group globally, return null to avoid duplication
        if (allProcessedGroups.Contains(groupId))
        {
            return null;
        }

        // Mark this group as globally processed
        allProcessedGroups.Add(groupId);

        // Add to current path for circular reference detection
        currentPath.Add(groupId);

        // Get the group entity
        var groupEntity = await _dbContext.SecurityGroups
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (groupEntity == null)
        {
            currentPath.Remove(groupId);
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

        // Recursively build parent groups
        var parentGroups = new List<SecurityGroup>();
        foreach (var parentGroupId in parentGroupIds)
        {
            // Use the same allProcessedGroups but a new path copy for each parent branch
            var newPath = new HashSet<string>(currentPath);
            var parentGroup = await BuildSecurityGroupWithParentsAsync(parentGroupId, allProcessedGroups, newPath);
            if (parentGroup != null)
            {
                parentGroups.Add(parentGroup);
            }
        }

        // Remove from current path before returning
        currentPath.Remove(groupId);

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
