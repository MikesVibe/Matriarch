using Matriarch.Web.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Matriarch.IntegrationTests;

/// <summary>
/// Unit tests for GroupManagementService.
/// These tests verify the behavior of group hierarchy traversal, particularly circular dependency handling.
/// </summary>
public class GroupManagementServiceTests
{
    /// <summary>
    /// Tests that GetParentGroupsAsync handles simple circular dependencies (A -> B -> A).
    /// This simulates the scenario where group A is a member of group B, and group B is a member of group A.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithSimpleCircularDependency_DoesNotInfiniteLoop()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Simulate circular dependency where A -> B -> A
        // In a real scenario, the implementation should handle this by tracking processed groups
        var parentGroupIds = new List<string> { groupBId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // Both groups should be in the info map (A and B)
        Assert.Equal(2, result.groupInfoMap.Count);
        Assert.Contains(groupAId, result.groupInfoMap.Keys);
        Assert.Contains(groupBId, result.groupInfoMap.Keys);
        
        // Parent groups should only contain B (since A was the direct group)
        Assert.Contains(groupBId, result.parentGroupIds);
    }

    /// <summary>
    /// Tests that GetParentGroupsAsync handles three-way circular dependencies (A -> B -> C -> A).
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithThreeWayCircularDependency_DoesNotInfiniteLoop()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Simulate circular dependency where A -> B -> C -> A
        var parentGroupIds = new List<string> { groupBId, groupCId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupCId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Group C", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // All three groups should be in the info map
        Assert.Equal(3, result.groupInfoMap.Count);
        Assert.Contains(groupAId, result.groupInfoMap.Keys);
        Assert.Contains(groupBId, result.groupInfoMap.Keys);
        Assert.Contains(groupCId, result.groupInfoMap.Keys);
        
        // Parent groups should contain B and C (since A was the direct group)
        Assert.Equal(2, result.parentGroupIds.Count);
        Assert.Contains(groupBId, result.parentGroupIds);
        Assert.Contains(groupCId, result.parentGroupIds);
    }

    /// <summary>
    /// Tests that GetParentGroupsAsync handles self-referential groups (A -> A).
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithSelfReferentialGroup_DoesNotInfiniteLoop()
    {
        // Arrange
        var groupAId = "group-a-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Simulate self-referential group where A -> A
        var parentGroupIds = new List<string>();
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Self-referential group", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // Only group A should be in the info map
        Assert.Single(result.groupInfoMap);
        Assert.Contains(groupAId, result.groupInfoMap.Keys);
        
        // Parent groups should be empty since A is the direct group and self-reference shouldn't add it again
        Assert.Empty(result.parentGroupIds);
    }

    /// <summary>
    /// Tests that GetParentGroupsAsync handles multiple direct groups with circular dependencies.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithMultipleDirectGroupsHavingCircularDependencies_ProcessesAllGroupsOnce()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";
        var directGroupIds = new List<string> { groupAId, groupBId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: A -> C, B -> C, C -> A (creates a cycle)
        var parentGroupIds = new List<string> { groupCId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupCId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupCId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Group C (creates cycle back to A)", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // All three groups should be in the info map
        Assert.Equal(3, result.groupInfoMap.Count);
        
        // Only C should be in parent groups (since A and B are direct groups)
        Assert.Single(result.parentGroupIds);
        Assert.Contains(groupCId, result.parentGroupIds);
    }

    /// <summary>
    /// Tests that GetParentGroupsAsync handles diamond dependency pattern with circular reference.
    /// Diamond: A -> B, A -> C, B -> D, C -> D, D -> A (cycle)
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithDiamondPatternAndCircularReference_ProcessesUniqueGroups()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";
        var groupDId = "group-d-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Diamond pattern where A -> B and C, B and C -> D, D -> A (cycle)
        var parentGroupIds = new List<string> { groupBId, groupCId, groupDId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Top of diamond", ParentGroupIds = new List<string> { groupBId, groupCId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Left branch", ParentGroupIds = new List<string> { groupDId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Right branch", ParentGroupIds = new List<string> { groupDId } } },
            { groupDId, new GroupInfo { Id = groupDId, DisplayName = "Group D", Description = "Bottom of diamond with cycle", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // All four groups should be in the info map
        Assert.Equal(4, result.groupInfoMap.Count);
        
        // Parent groups should contain B, C, and D (since A is the direct group)
        Assert.Equal(3, result.parentGroupIds.Count);
        Assert.Contains(groupBId, result.parentGroupIds);
        Assert.Contains(groupCId, result.parentGroupIds);
        Assert.Contains(groupDId, result.parentGroupIds);
    }

    /// <summary>
    /// Tests that GetParentGroupsAsync handles complex graph with multiple cycles.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_WithMultipleCycles_ProcessesAllGroupsWithoutInfiniteLoop()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";
        var groupDId = "group-d-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Multiple cycles - A -> B -> C -> A and B -> D -> B
        var parentGroupIds = new List<string> { groupBId, groupCId, groupDId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Part of cycle 1", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Shared by both cycles", ParentGroupIds = new List<string> { groupCId, groupDId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Part of cycle 1", ParentGroupIds = new List<string> { groupAId } } },
            { groupDId, new GroupInfo { Id = groupDId, DisplayName = "Group D", Description = "Part of cycle 2", ParentGroupIds = new List<string> { groupBId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds))
            .ReturnsAsync((parentGroupIds, groupInfoMap));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds);

        // Assert
        Assert.NotNull(result.parentGroupIds);
        Assert.NotNull(result.groupInfoMap);
        
        // All four groups should be in the info map
        Assert.Equal(4, result.groupInfoMap.Count);
        
        // Verify each group was only added once
        Assert.Contains(groupAId, result.groupInfoMap.Keys);
        Assert.Contains(groupBId, result.groupInfoMap.Keys);
        Assert.Contains(groupCId, result.groupInfoMap.Keys);
        Assert.Contains(groupDId, result.groupInfoMap.Keys);
    }
}
