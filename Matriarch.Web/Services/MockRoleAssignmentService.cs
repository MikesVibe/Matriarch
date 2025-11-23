using Matriarch.Shared.Models;

namespace Matriarch.Web.Services;

public interface IRoleAssignmentService
{
    Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(string identityInput);
}

public class MockRoleAssignmentService : IRoleAssignmentService
{
    private readonly List<RoleAssignment> _mockRoleAssignments;
    private readonly List<SecurityGroup> _mockSecurityGroups;
    
    public MockRoleAssignmentService()
    {
        // Create mock parent security groups
        var parentGroup1 = new SecurityGroup
        {
            Id = "parent-group-1",
            DisplayName = "All Azure Administrators",
            Description = "Parent group for all administrators",
            RoleAssignments = new List<RoleAssignment>
            {
                new RoleAssignment { Id = "ra-parent-1", RoleName = "Owner", Scope = "/subscriptions/sub-123", AssignedTo = "All Azure Administrators" },
                new RoleAssignment { Id = "ra-parent-2", RoleName = "User Access Administrator", Scope = "/subscriptions/sub-123", AssignedTo = "All Azure Administrators" }
            },
            ParentGroups = new List<SecurityGroup>()
        };

        var parentGroup2 = new SecurityGroup
        {
            Id = "parent-group-2",
            DisplayName = "Global Security Team",
            Description = "Top-level security group",
            RoleAssignments = new List<RoleAssignment>
            {
                new RoleAssignment { Id = "ra-parent-3", RoleName = "Security Admin", Scope = "/subscriptions/sub-123", AssignedTo = "Global Security Team" }
            },
            ParentGroups = new List<SecurityGroup>()
        };

        // Create mock security groups with parent group references
        _mockSecurityGroups = new List<SecurityGroup>
        {
            new SecurityGroup
            {
                Id = "group-1",
                DisplayName = "Application Developers",
                Description = "Developers working on application projects",
                RoleAssignments = new List<RoleAssignment>
                {
                    new RoleAssignment { Id = "ra-1", RoleName = "Contributor", Scope = "/subscriptions/sub-123/resourceGroups/rg-dev", AssignedTo = "Application Developers" },
                    new RoleAssignment { Id = "ra-2", RoleName = "Reader", Scope = "/subscriptions/sub-123", AssignedTo = "Application Developers" }
                },
                ParentGroups = new List<SecurityGroup> { parentGroup1 }
            },
            new SecurityGroup
            {
                Id = "group-2",
                DisplayName = "Database Administrators",
                Description = "DBAs with database access",
                RoleAssignments = new List<RoleAssignment>
                {
                    new RoleAssignment { Id = "ra-3", RoleName = "SQL DB Contributor", Scope = "/subscriptions/sub-123/resourceGroups/rg-databases", AssignedTo = "Database Administrators" },
                    new RoleAssignment { Id = "ra-4", RoleName = "Storage Account Contributor", Scope = "/subscriptions/sub-123/resourceGroups/rg-storage", AssignedTo = "Database Administrators" }
                },
                ParentGroups = new List<SecurityGroup> { parentGroup1, parentGroup2 }
            },
            new SecurityGroup
            {
                Id = "group-3",
                DisplayName = "Network Team",
                Description = "Network engineers and administrators",
                RoleAssignments = new List<RoleAssignment>
                {
                    new RoleAssignment { Id = "ra-5", RoleName = "Network Contributor", Scope = "/subscriptions/sub-123", AssignedTo = "Network Team" }
                },
                ParentGroups = new List<SecurityGroup>()
            }
        };

        // Create mock direct role assignments
        _mockRoleAssignments = new List<RoleAssignment>
        {
            new RoleAssignment { Id = "ra-direct-1", RoleName = "Virtual Machine Contributor", Scope = "/subscriptions/sub-123/resourceGroups/rg-compute", AssignedTo = "Direct Assignment" },
            new RoleAssignment { Id = "ra-direct-2", RoleName = "Key Vault Administrator", Scope = "/subscriptions/sub-123/resourceGroups/rg-keyvault", AssignedTo = "Direct Assignment" }
        };
    }

    public Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(string identityInput)
    {
        // For demo purposes, return mock data for any input
        // In a real implementation, this would query Azure or Neo4j
        
        var identity = new Identity
        {
            ObjectId = "12345678-1234-1234-1234-123456789abc",
            ApplicationId = "87654321-4321-4321-4321-cba987654321",
            Email = "user@example.com",
            Name = identityInput,
            Type = IdentityType.User
        };

        // Simulate that this user is member of the first two security groups
        var userSecurityGroups = _mockSecurityGroups.Take(2).ToList();

        // Mock API permissions
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

        var result = new IdentityRoleAssignmentResult
        {
            Identity = identity,
            DirectRoleAssignments = _mockRoleAssignments,
            SecurityGroups = userSecurityGroups,
            ApiPermissions = apiPermissions
        };

        return Task.FromResult(result);
    }
}
