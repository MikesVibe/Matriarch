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

        var identity = new Identity
        {
            ObjectId = spObjectId,
            Name = "Test Service Principal",
            Type = IdentityType.ServicePrincipal,
            ApplicationId = "app-id-123"
        };

        // Setup: SP is a direct member of group A
        var directGroupIds = new List<string> { groupAId };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);

        // Setup: Group A is a member of groups B and C (indirect/parent groups)
        var parentGroupIds = new List<string> { groupBId, groupCId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Direct group", ParentGroupIds = new List<string> { groupBId, groupCId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Parent group B", ParentGroupIds = new List<string>() } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Parent group C", ParentGroupIds = new List<string>() } }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Setup: Build security groups with pre-fetched data - return empty list as we're only testing FetchRoleAssignmentsForPrincipalsAsync
        _groupManagementServiceMock
            .Setup(s => s.BuildSecurityGroupsWithPreFetchedData(It.IsAny<List<string>>(), It.IsAny<Dictionary<string, GroupInfo>>(), It.IsAny<List<AzureRoleAssignmentDto>>()))
            .Returns(new List<SecurityGroup>());

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

        var service = new AzureRoleAssignmentService(
            _loggerMock.Object,
            _identityServiceMock.Object,
            _groupManagementServiceMock.Object,
            _apiPermissionsServiceMock.Object,
            _resourceGraphServiceMock.Object);

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

        var identity = new Identity
        {
            ObjectId = spObjectId,
            Name = "Isolated Service Principal",
            Type = IdentityType.ServicePrincipal,
            ApplicationId = "app-id-456"
        };

        // Setup: SP has no direct group memberships
        var directGroupIds = new List<string>();
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);

        // Setup: No parent groups
        var parentGroupIds = new List<string>();
        var groupInfoMap = new Dictionary<string, GroupInfo>();
        _groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Setup: Build security groups
        _groupManagementServiceMock
            .Setup(s => s.BuildSecurityGroupsWithPreFetchedData(It.IsAny<List<string>>(), It.IsAny<Dictionary<string, GroupInfo>>(), It.IsAny<List<AzureRoleAssignmentDto>>()))
            .Returns(new List<SecurityGroup>());

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

        var service = new AzureRoleAssignmentService(
            _loggerMock.Object,
            _identityServiceMock.Object,
            _groupManagementServiceMock.Object,
            _apiPermissionsServiceMock.Object,
            _resourceGraphServiceMock.Object);

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

        var identity = new Identity
        {
            ObjectId = spObjectId,
            Name = "Test Service Principal",
            Type = IdentityType.ServicePrincipal,
            ApplicationId = "app-id-789"
        };

        // Setup: SP is a direct member of groups A and B
        var directGroupIds = new List<string> { groupAId, groupBId };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);

        // Setup: Both groups A and B share the same parent group
        var parentGroupIds = new List<string> { sharedParentId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Direct group A", ParentGroupIds = new List<string> { sharedParentId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Direct group B", ParentGroupIds = new List<string> { sharedParentId } } },
            { sharedParentId, new GroupInfo { Id = sharedParentId, DisplayName = "Shared Parent", Description = "Shared parent group", ParentGroupIds = new List<string>() } }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Setup: Build security groups
        _groupManagementServiceMock
            .Setup(s => s.BuildSecurityGroupsWithPreFetchedData(It.IsAny<List<string>>(), It.IsAny<Dictionary<string, GroupInfo>>(), It.IsAny<List<AzureRoleAssignmentDto>>()))
            .Returns(new List<SecurityGroup>());

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

        var service = new AzureRoleAssignmentService(
            _loggerMock.Object,
            _identityServiceMock.Object,
            _groupManagementServiceMock.Object,
            _apiPermissionsServiceMock.Object,
            _resourceGraphServiceMock.Object);

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

        var identity = new Identity
        {
            ObjectId = spObjectId,
            Name = "Deeply Nested SP",
            Type = IdentityType.ServicePrincipal,
            ApplicationId = "app-id-deep"
        };

        // Setup: SP is a direct member of group level 1
        var directGroupIds = new List<string> { groupLevel1Id };
        _groupManagementServiceMock
            .Setup(s => s.GetGroupMembershipsAsync(It.Is<Identity>(i => i.ObjectId == spObjectId)))
            .ReturnsAsync(directGroupIds);

        // Setup: group level 1 -> level 2 -> level 3 (3 levels deep)
        var parentGroupIds = new List<string> { groupLevel2Id, groupLevel3Id };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupLevel1Id, new GroupInfo { Id = groupLevel1Id, DisplayName = "Level 1", Description = "First level", ParentGroupIds = new List<string> { groupLevel2Id } } },
            { groupLevel2Id, new GroupInfo { Id = groupLevel2Id, DisplayName = "Level 2", Description = "Second level", ParentGroupIds = new List<string> { groupLevel3Id } } },
            { groupLevel3Id, new GroupInfo { Id = groupLevel3Id, DisplayName = "Level 3", Description = "Third level", ParentGroupIds = new List<string>() } }
        };
        _groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Setup: Build security groups
        _groupManagementServiceMock
            .Setup(s => s.BuildSecurityGroupsWithPreFetchedData(It.IsAny<List<string>>(), It.IsAny<Dictionary<string, GroupInfo>>(), It.IsAny<List<AzureRoleAssignmentDto>>()))
            .Returns(new List<SecurityGroup>());

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

        var service = new AzureRoleAssignmentService(
            _loggerMock.Object,
            _identityServiceMock.Object,
            _groupManagementServiceMock.Object,
            _apiPermissionsServiceMock.Object,
            _resourceGraphServiceMock.Object);

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
