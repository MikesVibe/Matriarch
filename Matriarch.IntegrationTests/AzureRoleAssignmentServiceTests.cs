using Matriarch.Web.Models;
using Matriarch.Web.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Matriarch.IntegrationTests;

/// <summary>
/// Integration tests for AzureRoleAssignmentService.
/// These tests verify the behavior of role assignment fetching including group hierarchy resolution.
/// </summary>
public class AzureRoleAssignmentServiceTests
{
    private readonly Mock<ILogger<AzureRoleAssignmentService>> _loggerMock;
    private readonly Mock<IIdentityService> _identityServiceMock;
    private readonly Mock<IGroupManagementService> _groupManagementServiceMock;
    private readonly Mock<IApiPermissionsService> _apiPermissionsServiceMock;
    private readonly Mock<IResourceGraphService> _resourceGraphServiceMock;

    public AzureRoleAssignmentServiceTests()
    {
        _loggerMock = new Mock<ILogger<AzureRoleAssignmentService>>();
        _identityServiceMock = new Mock<IIdentityService>();
        _groupManagementServiceMock = new Mock<IGroupManagementService>();
        _apiPermissionsServiceMock = new Mock<IApiPermissionsService>();
        _resourceGraphServiceMock = new Mock<IResourceGraphService>();
    }

    private static Identity CreateServicePrincipalIdentity(string objectId, string name = "Test Service Principal", string appId = "app-id")
    {
        return new Identity
        {
            ObjectId = objectId,
            Name = name,
            Type = IdentityType.ServicePrincipal,
            ApplicationId = appId
        };
    }

    private AzureRoleAssignmentService CreateService()
    {
        return new AzureRoleAssignmentService(
            _loggerMock.Object,
            _identityServiceMock.Object,
            _groupManagementServiceMock.Object,
            _apiPermissionsServiceMock.Object,
            _resourceGraphServiceMock.Object);
    }

    /// <summary>
    /// Test that verifies when a Service Principal ObjectId is provided and the SP is a member of group A,
    /// which is in turn a member of groups B and C, then FetchRoleAssignmentsForPrincipalsAsync is called
    /// with all identities (SP ObjectId, group A, group B, and group C).
    /// </summary>
    [Fact]
    public async Task GetRoleAssignmentsAsync_WhenSpIsMemberOfNestedGroups_CallsFetchRoleAssignmentsForAllIdentities()
    {
        // Arrange
        var spObjectId = "sp-object-id-123";
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";

        var identity = CreateServicePrincipalIdentity(spObjectId, "Test Service Principal", "app-id-123");

        // Setup: SP is a direct member of group A
        var directGroupIds = new List<SecurityGroup> { new SecurityGroup { Id = groupAId, DisplayName = "Group A" } };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);
        
        // Setup: Transitive groups (Group B and C)
        var transitiveGroups = new List<SecurityGroup> 
        { 
            new SecurityGroup { Id = groupBId, DisplayName = "Group B" },
            new SecurityGroup { Id = groupCId, DisplayName = "Group C" }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetTransitiveGroupsAsync(It.IsAny<List<SecurityGroup>>()))
            .ReturnsAsync(transitiveGroups);



        // Setup: Resource graph service to capture the principal IDs
        List<string>? capturedPrincipalIds = null;
        _resourceGraphServiceMock
            .Setup(s => s.FetchRoleAssignmentsForPrincipalsAsync(It.IsAny<List<string>>()))
            .Callback<List<string>>(ids => capturedPrincipalIds = ids)
            .ReturnsAsync(new List<AzureRoleAssignmentDto>());

        // Setup: API permissions service returns empty list
        _apiPermissionsServiceMock
            .Setup(s => s.GetApiPermissionsAsync(It.IsAny<Identity>()))
            .ReturnsAsync(new List<ApiPermission>());

        var service = CreateService();

        // Act
        await service.GetRoleAssignmentsAsync(identity);

        // Assert
        Assert.NotNull(capturedPrincipalIds);

        // Verify that FetchRoleAssignmentsForPrincipalsAsync was called exactly once
        _resourceGraphServiceMock.Verify(
            s => s.FetchRoleAssignmentsForPrincipalsAsync(It.IsAny<List<string>>()),
            Times.Once);

        // Verify all expected identities are included
        Assert.Contains(spObjectId, capturedPrincipalIds); // SP ObjectId
        Assert.Contains(groupAId, capturedPrincipalIds);   // Direct group
        Assert.Contains(groupBId, capturedPrincipalIds);   // Parent group B
        Assert.Contains(groupCId, capturedPrincipalIds);   // Parent group C

        // Verify total count is correct (SP + group A + group B + group C = 4)
        Assert.Equal(4, capturedPrincipalIds.Count);
    }

    /// <summary>
    /// Test that verifies when a SP has no group memberships, only the SP ObjectId is passed to FetchRoleAssignmentsForPrincipalsAsync.
    /// </summary>
    [Fact]
    public async Task GetRoleAssignmentsAsync_WhenSpHasNoGroups_CallsFetchRoleAssignmentsWithOnlySpId()
    {
        // Arrange
        var spObjectId = "sp-object-id-456";

        var identity = CreateServicePrincipalIdentity(spObjectId, "Isolated Service Principal", "app-id-456");

        // Setup: SP has no direct group memberships
        var directGroupIds = new List<SecurityGroup>();
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);
        
        // Setup: No transitive groups
        var transitiveGroups = new List<SecurityGroup>();
        _groupManagementServiceMock
            .Setup(s => s.GetTransitiveGroupsAsync(It.IsAny<List<SecurityGroup>>()))
            .ReturnsAsync(transitiveGroups);



        // Setup: Resource graph service to capture the principal IDs
        List<string>? capturedPrincipalIds = null;
        _resourceGraphServiceMock
            .Setup(s => s.FetchRoleAssignmentsForPrincipalsAsync(It.IsAny<List<string>>()))
            .Callback<List<string>>(ids => capturedPrincipalIds = ids)
            .ReturnsAsync(new List<AzureRoleAssignmentDto>());

        // Setup: API permissions service returns empty list
        _apiPermissionsServiceMock
            .Setup(s => s.GetApiPermissionsAsync(It.IsAny<Identity>()))
            .ReturnsAsync(new List<ApiPermission>());

        var service = CreateService();

        // Act
        await service.GetRoleAssignmentsAsync(identity);

        // Assert
        Assert.NotNull(capturedPrincipalIds);
        Assert.Single(capturedPrincipalIds);
        Assert.Equal(spObjectId, capturedPrincipalIds[0]);
    }

    /// <summary>
    /// Test that verifies when a SP is member of multiple direct groups that share parent groups,
    /// duplicate group IDs are not passed to FetchRoleAssignmentsForPrincipalsAsync.
    /// </summary>
    [Fact]
    public async Task GetRoleAssignmentsAsync_WhenGroupsHaveSharedParents_IncludesUniqueGroupIds()
    {
        // Arrange
        var spObjectId = "sp-object-id-789";
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var sharedParentId = "shared-parent-id";

        var identity = CreateServicePrincipalIdentity(spObjectId, "Test Service Principal", "app-id-789");

        // Setup: SP is a direct member of groups A and B
        var directGroupIds = new List<SecurityGroup> 
        { 
            new SecurityGroup { Id = groupAId, DisplayName = "Group A" },
            new SecurityGroup { Id = groupBId, DisplayName = "Group B" }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);
        
        // Setup: Transitive groups (shared parent)
        var transitiveGroups = new List<SecurityGroup> 
        { 
            new SecurityGroup { Id = sharedParentId, DisplayName = "Shared Parent" }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetTransitiveGroupsAsync(It.IsAny<List<SecurityGroup>>()))
            .ReturnsAsync(transitiveGroups);



        // Setup: Resource graph service to capture the principal IDs
        List<string>? capturedPrincipalIds = null;
        _resourceGraphServiceMock
            .Setup(s => s.FetchRoleAssignmentsForPrincipalsAsync(It.IsAny<List<string>>()))
            .Callback<List<string>>(ids => capturedPrincipalIds = ids)
            .ReturnsAsync(new List<AzureRoleAssignmentDto>());

        // Setup: API permissions service returns empty list
        _apiPermissionsServiceMock
            .Setup(s => s.GetApiPermissionsAsync(It.IsAny<Identity>()))
            .ReturnsAsync(new List<ApiPermission>());

        var service = CreateService();

        // Act
        await service.GetRoleAssignmentsAsync(identity);

        // Assert
        Assert.NotNull(capturedPrincipalIds);

        // Verify all expected identities are included
        Assert.Contains(spObjectId, capturedPrincipalIds);     // SP ObjectId
        Assert.Contains(groupAId, capturedPrincipalIds);       // Direct group A
        Assert.Contains(groupBId, capturedPrincipalIds);       // Direct group B  
        Assert.Contains(sharedParentId, capturedPrincipalIds); // Shared parent group

        // Verify total count is correct (SP + group A + group B + shared parent = 4)
        // Note: The current implementation may include duplicates, but this test verifies the expected behavior
        Assert.Equal(4, capturedPrincipalIds.Count);
    }

    /// <summary>
    /// Test that verifies the service correctly processes deeply nested group hierarchies.
    /// </summary>
    [Fact]
    public async Task GetRoleAssignmentsAsync_WithDeepGroupHierarchy_CallsFetchRoleAssignmentsForAllLevels()
    {
        // Arrange
        var spObjectId = "sp-object-id-deep";
        var groupLevel1Id = "group-level-1";
        var groupLevel2Id = "group-level-2";
        var groupLevel3Id = "group-level-3";

        var identity = CreateServicePrincipalIdentity(spObjectId, "Deeply Nested SP", "app-id-deep");

        // Setup: SP is a direct member of group level 1
        var directGroupIds = new List<SecurityGroup> 
        { 
            new SecurityGroup { Id = groupLevel1Id, DisplayName = "Group Level 1" }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);
        
        // Setup: Transitive groups (levels 2 and 3)
        var transitiveGroups = new List<SecurityGroup> 
        { 
            new SecurityGroup { Id = groupLevel2Id, DisplayName = "Group Level 2" },
            new SecurityGroup { Id = groupLevel3Id, DisplayName = "Group Level 3" }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetTransitiveGroupsAsync(It.IsAny<List<SecurityGroup>>()))
            .ReturnsAsync(transitiveGroups);



        // Setup: Resource graph service to capture the principal IDs
        List<string>? capturedPrincipalIds = null;
        _resourceGraphServiceMock
            .Setup(s => s.FetchRoleAssignmentsForPrincipalsAsync(It.IsAny<List<string>>()))
            .Callback<List<string>>(ids => capturedPrincipalIds = ids)
            .ReturnsAsync(new List<AzureRoleAssignmentDto>());

        // Setup: API permissions service returns empty list
        _apiPermissionsServiceMock
            .Setup(s => s.GetApiPermissionsAsync(It.IsAny<Identity>()))
            .ReturnsAsync(new List<ApiPermission>());

        var service = CreateService();

        // Act
        await service.GetRoleAssignmentsAsync(identity);

        // Assert
        Assert.NotNull(capturedPrincipalIds);

        // Verify all levels are included
        Assert.Contains(spObjectId, capturedPrincipalIds);
        Assert.Contains(groupLevel1Id, capturedPrincipalIds);
        Assert.Contains(groupLevel2Id, capturedPrincipalIds);
        Assert.Contains(groupLevel3Id, capturedPrincipalIds);

        // Verify total count is correct (SP + 3 group levels = 4)
        Assert.Equal(4, capturedPrincipalIds.Count);
    }
}
