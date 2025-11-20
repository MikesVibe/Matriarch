using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Matriarch.Configuration;
using Matriarch.Data;
using Matriarch.Models;
using Matriarch.Services;
using Xunit;

namespace Matriarch.Tests;

public class SqliteServiceTests
{
    private SqliteService CreateSqliteService(out MatriarchDbContext context)
    {
        var options = new DbContextOptionsBuilder<MatriarchDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        context = new MatriarchDbContext(options);
        var logger = new LoggerFactory().CreateLogger<SqliteService>();
        return new SqliteService(logger, context);
    }

    [Fact]
    public async Task InitializeDatabaseAsync_ShouldCreateDatabase()
    {
        // Arrange
        var service = CreateSqliteService(out var context);

        // Act
        await service.InitializeDatabaseAsync();

        // Assert
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task StoreDataAsync_ShouldStoreAllEntities()
    {
        // Arrange
        var service = CreateSqliteService(out var context);
        await service.InitializeDatabaseAsync();

        var appRegistrations = new List<AppRegistrationDto>
        {
            new AppRegistrationDto
            {
                Id = "app-reg-1",
                AppId = "app-id-1",
                DisplayName = "Test App",
                ServicePrincipalId = "sp-1"
            }
        };

        var enterpriseApps = new List<EnterpriseApplicationDto>
        {
            new EnterpriseApplicationDto
            {
                Id = "sp-1",
                AppId = "app-id-1",
                DisplayName = "Test Enterprise App",
                GroupMemberships = new List<string> { "group-1" }
            }
        };

        var securityGroups = new List<SecurityGroupDto>
        {
            new SecurityGroupDto
            {
                Id = "group-1",
                DisplayName = "Test Group",
                Description = "Test Description"
            }
        };

        var roleAssignments = new List<RoleAssignmentDto>
        {
            new RoleAssignmentDto
            {
                Id = "ra-1",
                PrincipalId = "sp-1",
                PrincipalType = "ServicePrincipal",
                RoleDefinitionId = "role-def-1",
                RoleName = "Contributor",
                Scope = "/subscriptions/test"
            }
        };

        // Act
        await service.StoreDataAsync(appRegistrations, enterpriseApps, securityGroups, roleAssignments);

        // Assert
        Assert.Equal(1, await context.AppRegistrations.CountAsync());
        Assert.Equal(1, await context.EnterpriseApplications.CountAsync());
        Assert.Equal(1, await context.SecurityGroups.CountAsync());
        Assert.Equal(1, await context.RoleAssignments.CountAsync());
        Assert.Equal(1, await context.GroupMemberships.CountAsync());
    }

    [Fact]
    public async Task GetIdentityRoleAssignmentsAsync_ShouldHandleCircularGroupMembership()
    {
        // Arrange
        var service = CreateSqliteService(out var context);
        await service.InitializeDatabaseAsync();

        // Create a circular group membership scenario: Group A -> B -> C -> A
        var enterpriseApps = new List<EnterpriseApplicationDto>
        {
            new EnterpriseApplicationDto
            {
                Id = "sp-1",
                AppId = "app-id-1",
                DisplayName = "Test Enterprise App",
                GroupMemberships = new List<string>() // Will be added manually
            }
        };

        var securityGroups = new List<SecurityGroupDto>
        {
            new SecurityGroupDto
            {
                Id = "group-a",
                DisplayName = "Group A",
                Description = "Group A is member of B",
                Members = new List<GroupMemberDto>()
            },
            new SecurityGroupDto
            {
                Id = "group-b",
                DisplayName = "Group B",
                Description = "Group B is member of C",
                Members = new List<GroupMemberDto>()
            },
            new SecurityGroupDto
            {
                Id = "group-c",
                DisplayName = "Group C",
                Description = "Group C is member of A (creates circular reference)",
                Members = new List<GroupMemberDto>()
            }
        };

        var roleAssignments = new List<RoleAssignmentDto>
        {
            new RoleAssignmentDto
            {
                Id = "ra-1",
                PrincipalId = "group-a",
                PrincipalType = "Group",
                RoleDefinitionId = "role-def-1",
                RoleName = "Reader on Group A",
                Scope = "/subscriptions/test"
            },
            new RoleAssignmentDto
            {
                Id = "ra-2",
                PrincipalId = "group-b",
                PrincipalType = "Group",
                RoleDefinitionId = "role-def-2",
                RoleName = "Contributor on Group B",
                Scope = "/subscriptions/test"
            },
            new RoleAssignmentDto
            {
                Id = "ra-3",
                PrincipalId = "group-c",
                PrincipalType = "Group",
                RoleDefinitionId = "role-def-3",
                RoleName = "Owner on Group C",
                Scope = "/subscriptions/test"
            }
        };

        await service.StoreDataAsync(new List<AppRegistrationDto>(), enterpriseApps, securityGroups, roleAssignments);

        // Manually create the circular group memberships in the database
        // SP-1 is member of Group A
        context.GroupMemberships.Add(new GroupMembershipEntity
        {
            MemberId = "sp-1",
            GroupId = "group-a",
            MemberType = "EnterpriseApplication"
        });
        // Group A is member of B
        context.GroupMemberships.Add(new GroupMembershipEntity
        {
            MemberId = "group-a",
            GroupId = "group-b",
            MemberType = "SecurityGroup"
        });
        // Group B is member of C
        context.GroupMemberships.Add(new GroupMembershipEntity
        {
            MemberId = "group-b",
            GroupId = "group-c",
            MemberType = "SecurityGroup"
        });
        // Group C is member of A (creates circular reference)
        context.GroupMemberships.Add(new GroupMembershipEntity
        {
            MemberId = "group-c",
            GroupId = "group-a",
            MemberType = "SecurityGroup"
        });
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetIdentityRoleAssignmentsAsync("sp-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("sp-1", result.Identity.ObjectId);
        
        // The service should return Group A without entering an infinite loop
        Assert.Single(result.SecurityGroups);
        Assert.Equal("group-a", result.SecurityGroups[0].Id);
        
        // Verify that Group A's role assignments are present
        Assert.Single(result.SecurityGroups[0].RoleAssignments);
        Assert.Equal("Reader on Group A", result.SecurityGroups[0].RoleAssignments[0].RoleName);

        // Verify that parent groups are discovered (but circular reference is prevented)
        // Group A should have Group B as parent
        Assert.Single(result.SecurityGroups[0].ParentGroups);
        Assert.Equal("group-b", result.SecurityGroups[0].ParentGroups[0].Id);

        // Group B should have Group C as parent
        Assert.Single(result.SecurityGroups[0].ParentGroups[0].ParentGroups);
        Assert.Equal("group-c", result.SecurityGroups[0].ParentGroups[0].ParentGroups[0].Id);

        // Group C should NOT have Group A as parent (circular reference prevented)
        Assert.Empty(result.SecurityGroups[0].ParentGroups[0].ParentGroups[0].ParentGroups);
    }

    [Fact]
    public async Task GetIdentityRoleAssignmentsAsync_ShouldReturnDirectRoleAssignments()
    {
        // Arrange
        var service = CreateSqliteService(out var context);
        await service.InitializeDatabaseAsync();

        var enterpriseApps = new List<EnterpriseApplicationDto>
        {
            new EnterpriseApplicationDto
            {
                Id = "sp-1",
                AppId = "app-id-1",
                DisplayName = "Test Enterprise App",
                RoleAssignments = new List<RoleAssignmentDto>
                {
                    new RoleAssignmentDto
                    {
                        Id = "ra-1",
                        PrincipalId = "sp-1",
                        PrincipalType = "ServicePrincipal",
                        RoleDefinitionId = "role-def-1",
                        RoleName = "Contributor",
                        Scope = "/subscriptions/test"
                    }
                }
            }
        };

        await service.StoreDataAsync(new List<AppRegistrationDto>(), enterpriseApps, new List<SecurityGroupDto>(), new List<RoleAssignmentDto>());

        // Act
        var result = await service.GetIdentityRoleAssignmentsAsync("sp-1");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.DirectRoleAssignments);
        Assert.Equal("Contributor", result.DirectRoleAssignments[0].RoleName);
    }

    [Fact]
    public async Task GetIdentityRoleAssignmentsAsync_ShouldReturnNullForNonExistentIdentity()
    {
        // Arrange
        var service = CreateSqliteService(out var context);
        await service.InitializeDatabaseAsync();

        // Act
        var result = await service.GetIdentityRoleAssignmentsAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }
}
