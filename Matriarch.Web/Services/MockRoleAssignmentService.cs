using Matriarch.Web.Models;

namespace Matriarch.Web.Services;

public interface IRoleAssignmentService
{
    Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(Identity identity);
    Task<(IdentityRoleAssignmentResult result, TimeSpan elapsedTime)> GetRoleAssignmentsAsync(Identity identity, bool useParallelProcessing);
    Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput);
    
    // Separate methods for sequential loading
    Task<List<RoleAssignment>> GetDirectRoleAssignmentsAsync(Identity identity);
    Task<List<SecurityGroup>> GetDirectGroupsAsync(Identity identity);
    Task<List<SecurityGroup>> GetIndirectGroupsAsync(List<SecurityGroup> directGroups);
    Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity);
    Task PopulateGroupRoleAssignmentsAsync(List<SecurityGroup> directGroups, List<SecurityGroup> indirectGroups);
}

public class MockRoleAssignmentService : IRoleAssignmentService
{
    private readonly IApiPermissionsService _apiPermissionsService;
    private readonly List<RoleAssignment> _mockRoleAssignments;
    private readonly List<SecurityGroup> _mockSecurityGroups;
    
    public MockRoleAssignmentService(IApiPermissionsService apiPermissionsService)
    {
        _apiPermissionsService = apiPermissionsService;

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

    public async Task<IdentityRoleAssignmentResult> GetRoleAssignmentsAsync(Identity identity)
    {
        var (result, _) = await GetRoleAssignmentsAsync(identity, false);
        return result;
    }

    public async Task<(IdentityRoleAssignmentResult result, TimeSpan elapsedTime)> GetRoleAssignmentsAsync(Identity identity, bool useParallelProcessing)
    {
        // For demo purposes, return mock data for the provided identity
        // In a real implementation, this would query Azure or Neo4j
        
        // Simulate that this user is member of the first two security groups
        var userSecurityGroups = _mockSecurityGroups.Take(2).ToList();

        // Get mock API permissions from the service (will return empty list for User type)
        var apiPermissions = await _apiPermissionsService.GetApiPermissionsAsync(identity);

        var result = new IdentityRoleAssignmentResult
        {
            Identity = identity,
            DirectRoleAssignments = _mockRoleAssignments,
            //SecurityGroups = userSecurityGroups,
            ApiPermissions = apiPermissions
        };

        return (result, TimeSpan.Zero);
    }

    public Task<IdentitySearchResult> SearchIdentitiesAsync(string searchInput)
    {
        // For demo purposes, return empty search results
        var searchResult = new IdentitySearchResult
        {
            Identities = new List<Identity>()
        };

        return Task.FromResult(searchResult);
    }

    public Task<List<RoleAssignment>> GetDirectRoleAssignmentsAsync(Identity identity)
    {
        return Task.FromResult(_mockRoleAssignments);
    }

    public Task<List<SecurityGroup>> GetDirectGroupsAsync(Identity identity)
    {
        return Task.FromResult(_mockSecurityGroups.Take(2).ToList());
    }

    public Task<List<SecurityGroup>> GetIndirectGroupsAsync(List<SecurityGroup> directGroups)
    {
        // Return parent groups from the mock data
        return Task.FromResult(_mockSecurityGroups.Skip(2).ToList());
    }

    public async Task<List<ApiPermission>> GetApiPermissionsAsync(Identity identity)
    {
        return await _apiPermissionsService.GetApiPermissionsAsync(identity);
    }

    public Task PopulateGroupRoleAssignmentsAsync(List<SecurityGroup> directGroups, List<SecurityGroup> indirectGroups)
    {
        // Mock service already has role assignments populated
        return Task.CompletedTask;
    }
}
