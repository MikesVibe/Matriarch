using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Matriarch.Configuration;
using Matriarch.Data;
using Matriarch.Models;
using Matriarch.Shared.Models;
using System.Text.Json;

namespace Matriarch.Services;

public class SqliteService : ISqliteService
{
    private readonly ILogger<SqliteService> _logger;
    private readonly MatriarchDbContext _dbContext;

    public SqliteService(ILogger<SqliteService> logger, MatriarchDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task InitializeDatabaseAsync()
    {
        _logger.LogInformation("Initializing SQLite database...");
        await _dbContext.Database.EnsureCreatedAsync();
        _logger.LogInformation("SQLite database initialized successfully");
    }

    public async Task StoreDataAsync(
        List<AppRegistrationDto> appRegistrations,
        List<EnterpriseApplicationDto> enterpriseApps,
        List<SecurityGroupDto> securityGroups,
        List<RoleAssignmentDto> roleAssignments)
    {
        _logger.LogInformation("Storing data to SQLite database...");

        // Clear existing data
        _dbContext.AppRegistrations.RemoveRange(_dbContext.AppRegistrations);
        _dbContext.EnterpriseApplications.RemoveRange(_dbContext.EnterpriseApplications);
        _dbContext.SecurityGroups.RemoveRange(_dbContext.SecurityGroups);
        _dbContext.RoleAssignments.RemoveRange(_dbContext.RoleAssignments);
        _dbContext.FederatedCredentials.RemoveRange(_dbContext.FederatedCredentials);
        _dbContext.GroupMemberships.RemoveRange(_dbContext.GroupMemberships);

        await _dbContext.SaveChangesAsync();

        // Store App Registrations
        foreach (var appReg in appRegistrations)
        {
            var entity = new AppRegistrationEntity
            {
                Id = appReg.Id,
                AppId = appReg.AppId,
                DisplayName = appReg.DisplayName,
                ServicePrincipalId = appReg.ServicePrincipalId
            };
            _dbContext.AppRegistrations.Add(entity);

            // Store federated credentials
            foreach (var fedCred in appReg.FederatedCredentials)
            {
                var credEntity = new FederatedCredentialEntity
                {
                    Id = fedCred.Id,
                    AppRegistrationId = appReg.Id,
                    Name = fedCred.Name,
                    Issuer = fedCred.Issuer,
                    Subject = fedCred.Subject,
                    Audiences = JsonSerializer.Serialize(fedCred.Audiences)
                };
                _dbContext.FederatedCredentials.Add(credEntity);
            }
        }

        // Store Enterprise Applications
        foreach (var app in enterpriseApps)
        {
            var entity = new EnterpriseApplicationEntity
            {
                Id = app.Id,
                AppId = app.AppId,
                DisplayName = app.DisplayName
            };
            _dbContext.EnterpriseApplications.Add(entity);

            // Store group memberships for enterprise apps
            foreach (var groupId in app.GroupMemberships)
            {
                var membership = new GroupMembershipEntity
                {
                    MemberId = app.Id,
                    GroupId = groupId,
                    MemberType = "EnterpriseApplication"
                };
                _dbContext.GroupMemberships.Add(membership);
            }

            // Store role assignments for enterprise apps
            foreach (var roleAssignment in app.RoleAssignments)
            {
                var raEntity = new RoleAssignmentEntity
                {
                    Id = roleAssignment.Id,
                    PrincipalId = roleAssignment.PrincipalId,
                    PrincipalType = roleAssignment.PrincipalType,
                    RoleDefinitionId = roleAssignment.RoleDefinitionId,
                    RoleName = roleAssignment.RoleName,
                    Scope = roleAssignment.Scope
                };
                _dbContext.RoleAssignments.Add(raEntity);
            }
        }

        // Store Security Groups
        foreach (var group in securityGroups)
        {
            var entity = new SecurityGroupEntity
            {
                Id = group.Id,
                DisplayName = group.DisplayName,
                Description = group.Description
            };
            _dbContext.SecurityGroups.Add(entity);

            // Store group-to-group memberships
            foreach (var member in group.Members.Where(m => m.Type == MemberType.Group))
            {
                var membership = new GroupMembershipEntity
                {
                    MemberId = member.Id,
                    GroupId = group.Id,
                    MemberType = "SecurityGroup"
                };
                _dbContext.GroupMemberships.Add(membership);
            }
        }

        // Store Role Assignments
        foreach (var ra in roleAssignments)
        {
            // Skip if already added via enterprise apps
            if (_dbContext.RoleAssignments.Local.Any(e => e.Id == ra.Id))
                continue;

            var entity = new RoleAssignmentEntity
            {
                Id = ra.Id,
                PrincipalId = ra.PrincipalId,
                PrincipalType = ra.PrincipalType,
                RoleDefinitionId = ra.RoleDefinitionId,
                RoleName = ra.RoleName,
                Scope = ra.Scope
            };
            _dbContext.RoleAssignments.Add(entity);
        }

        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation(
            "Stored data to SQLite: {AppRegs} app registrations, {EnterpriseApps} enterprise apps, {SecurityGroups} security groups, {RoleAssignments} role assignments",
            appRegistrations.Count, enterpriseApps.Count, securityGroups.Count, roleAssignments.Count);
    }

    public async Task<IdentityRoleAssignmentResult?> GetIdentityRoleAssignmentsAsync(string objectId)
    {
        _logger.LogInformation("Retrieving role assignments for identity: {ObjectId}", objectId);

        // Find the identity - could be an enterprise app or user (for simplicity, treating as enterprise app)
        var enterpriseApp = await _dbContext.EnterpriseApplications
            .FirstOrDefaultAsync(e => e.Id == objectId);

        if (enterpriseApp == null)
        {
            _logger.LogWarning("Identity not found: {ObjectId}", objectId);
            return null;
        }

        var result = new IdentityRoleAssignmentResult
        {
            Identity = new Identity
            {
                ObjectId = enterpriseApp.Id,
                ApplicationId = enterpriseApp.AppId,
                Name = enterpriseApp.DisplayName
            }
        };

        // Get direct role assignments
        var directRoleAssignments = await _dbContext.RoleAssignments
            .Where(ra => ra.PrincipalId == objectId)
            .ToListAsync();

        result.DirectRoleAssignments = directRoleAssignments.Select(ra => new RoleAssignment
        {
            Id = ra.Id,
            RoleName = ra.RoleName,
            Scope = ra.Scope,
            AssignedTo = "Direct Assignment"
        }).ToList();

        // Get security groups the identity is a member of (with circular reference detection)
        var securityGroups = await GetSecurityGroupsWithRoleAssignmentsAsync(objectId);
        result.SecurityGroups = securityGroups;

        _logger.LogInformation(
            "Retrieved {DirectCount} direct role assignments and {GroupCount} security groups for {ObjectId}",
            result.DirectRoleAssignments.Count, result.SecurityGroups.Count, objectId);

        return result;
    }

    private async Task<List<SecurityGroup>> GetSecurityGroupsWithRoleAssignmentsAsync(string memberId)
    {
        var result = new List<SecurityGroup>();
        var visitedGroups = new HashSet<string>();

        // Get immediate groups the member belongs to
        var immediateGroupIds = await _dbContext.GroupMemberships
            .Where(gm => gm.MemberId == memberId)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        foreach (var groupId in immediateGroupIds)
        {
            var securityGroup = await GetSecurityGroupRecursiveAsync(groupId, visitedGroups);
            if (securityGroup != null)
            {
                result.Add(securityGroup);
            }
        }

        return result;
    }

    private async Task<SecurityGroup?> GetSecurityGroupRecursiveAsync(string groupId, HashSet<string> visitedGroups)
    {
        // Circular reference detection - if we've already visited this group, skip it
        if (visitedGroups.Contains(groupId))
        {
            _logger.LogWarning("Circular group membership detected for group: {GroupId}", groupId);
            return null;
        }

        visitedGroups.Add(groupId);

        var groupEntity = await _dbContext.SecurityGroups
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (groupEntity == null)
        {
            return null;
        }

        var securityGroup = new SecurityGroup
        {
            Id = groupEntity.Id,
            DisplayName = groupEntity.DisplayName,
            Description = groupEntity.Description ?? string.Empty,
            RoleAssignments = new List<RoleAssignment>(),
            ParentGroups = new List<SecurityGroup>()
        };

        // Get role assignments for this group
        var roleAssignments = await _dbContext.RoleAssignments
            .Where(ra => ra.PrincipalId == groupId && ra.PrincipalType == "Group")
            .ToListAsync();

        securityGroup.RoleAssignments = roleAssignments.Select(ra => new RoleAssignment
        {
            Id = ra.Id,
            RoleName = ra.RoleName,
            Scope = ra.Scope,
            AssignedTo = groupEntity.DisplayName
        }).ToList();

        // Get parent groups (groups that this group is a member of)
        var parentGroupIds = await _dbContext.GroupMemberships
            .Where(gm => gm.MemberId == groupId && gm.MemberType == "SecurityGroup")
            .Select(gm => gm.GroupId)
            .ToListAsync();

        foreach (var parentGroupId in parentGroupIds)
        {
            var parentGroup = await GetSecurityGroupRecursiveAsync(parentGroupId, visitedGroups);
            if (parentGroup != null)
            {
                securityGroup.ParentGroups.Add(parentGroup);
            }
        }

        return securityGroup;
    }
}
