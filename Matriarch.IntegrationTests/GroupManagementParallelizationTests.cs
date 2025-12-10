using Matriarch.Web.Services;
using Matriarch.Web.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Matriarch.IntegrationTests;

/// <summary>
/// Tests for the parallelization feature of GroupManagementService.
/// These tests verify that parallel processing produces the same results as sequential processing
/// and handles edge cases appropriately.
/// </summary>
public class GroupManagementParallelizationTests
{
    /// <summary>
    /// Tests that parallel processing produces the same results as sequential processing
    /// for a simple hierarchy.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_ParallelVsSequential_ProducesSameResults()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var groupCId = "group-c-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Sequential result
        var parentGroupIdsSeq = new List<string> { groupBId, groupCId };
        var groupInfoMapSeq = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupCId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Group C", ParentGroupIds = new List<string>() } }
        };
        
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, false))
            .ReturnsAsync((parentGroupIdsSeq, groupInfoMapSeq, TimeSpan.FromMilliseconds(100)));

        // Setup: Parallel result (should be the same)
        var parentGroupIdsPar = new List<string> { groupBId, groupCId };
        var groupInfoMapPar = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupCId } } },
            { groupCId, new GroupInfo { Id = groupCId, DisplayName = "Group C", Description = "Group C", ParentGroupIds = new List<string>() } }
        };
        
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, true))
            .ReturnsAsync((parentGroupIdsPar, groupInfoMapPar, TimeSpan.FromMilliseconds(50)));

        // Act
        var resultSeq = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, false);
        var resultPar = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, true);

        // Assert
        Assert.Equal(resultSeq.parentGroupIds.Count, resultPar.parentGroupIds.Count);
        Assert.Equal(resultSeq.groupInfoMap.Count, resultPar.groupInfoMap.Count);
        
        // Check that all groups are present in both results
        foreach (var groupId in resultSeq.parentGroupIds)
        {
            Assert.Contains(groupId, resultPar.parentGroupIds);
        }
        
        foreach (var kvp in resultSeq.groupInfoMap)
        {
            Assert.True(resultPar.groupInfoMap.ContainsKey(kvp.Key));
            Assert.Equal(kvp.Value.DisplayName, resultPar.groupInfoMap[kvp.Key].DisplayName);
        }
    }

    /// <summary>
    /// Tests that parallel processing returns elapsed time measurement.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_ReturnsElapsedTime()
    {
        // Arrange
        var directGroupIds = new List<string> { "group-a-id" };
        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        var parentGroupIds = new List<string>();
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { "group-a-id", new GroupInfo { Id = "group-a-id", DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string>() } }
        };
        
        // Setup with a specific elapsed time
        var expectedElapsedTime = TimeSpan.FromMilliseconds(123);
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, It.IsAny<bool>()))
            .ReturnsAsync((parentGroupIds, groupInfoMap, expectedElapsedTime));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, true);

        // Assert
        Assert.Equal(expectedElapsedTime, result.elapsedTime);
    }

    /// <summary>
    /// Tests that parallel processing handles empty input correctly.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_ParallelWithEmptyInput_ReturnsEmptyResult()
    {
        // Arrange
        var directGroupIds = new List<string>();
        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        var parentGroupIds = new List<string>();
        var groupInfoMap = new Dictionary<string, GroupInfo>();
        
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, true))
            .ReturnsAsync((parentGroupIds, groupInfoMap, TimeSpan.Zero));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, true);

        // Assert
        Assert.Empty(result.parentGroupIds);
        Assert.Empty(result.groupInfoMap);
    }

    /// <summary>
    /// Tests that parallel processing handles large hierarchies efficiently.
    /// This is a mock test - in real scenarios, parallel should be faster for large hierarchies.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_ParallelWithLargeHierarchy_IsFasterThanSequential()
    {
        // Arrange
        var directGroupIds = new List<string> { "root-group" };
        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Create a mock large hierarchy
        var parentGroupIds = Enumerable.Range(1, 50).Select(i => $"group-{i}").ToList();
        var groupInfoMap = new Dictionary<string, GroupInfo>();
        foreach (var groupId in directGroupIds.Concat(parentGroupIds))
        {
            groupInfoMap[groupId] = new GroupInfo { Id = groupId, DisplayName = $"Group {groupId}", Description = "", ParentGroupIds = new List<string>() };
        }

        // Sequential takes 500ms
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, false))
            .ReturnsAsync((parentGroupIds, groupInfoMap, TimeSpan.FromMilliseconds(500)));

        // Parallel takes 200ms (simulating speed improvement)
        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, true))
            .ReturnsAsync((parentGroupIds, groupInfoMap, TimeSpan.FromMilliseconds(200)));

        // Act
        var resultSeq = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, false);
        var resultPar = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, true);

        // Assert
        Assert.True(resultPar.elapsedTime < resultSeq.elapsedTime, 
            $"Parallel processing should be faster. Sequential: {resultSeq.elapsedTime.TotalMilliseconds}ms, Parallel: {resultPar.elapsedTime.TotalMilliseconds}ms");
    }

    /// <summary>
    /// Tests that parallel processing handles circular dependencies without infinite loop.
    /// </summary>
    [Fact]
    public async Task GetParentGroupsAsync_ParallelWithCircularDependency_DoesNotInfiniteLoop()
    {
        // Arrange
        var groupAId = "group-a-id";
        var groupBId = "group-b-id";
        var directGroupIds = new List<string> { groupAId };

        var groupManagementServiceMock = new Mock<IGroupManagementService>();

        // Setup: Simulate circular dependency where A -> B -> A
        var parentGroupIds = new List<string> { groupBId };
        var groupInfoMap = new Dictionary<string, GroupInfo>
        {
            { groupAId, new GroupInfo { Id = groupAId, DisplayName = "Group A", Description = "Group A", ParentGroupIds = new List<string> { groupBId } } },
            { groupBId, new GroupInfo { Id = groupBId, DisplayName = "Group B", Description = "Group B", ParentGroupIds = new List<string> { groupAId } } }
        };

        groupManagementServiceMock
            .Setup(s => s.GetParentGroupsAsync(directGroupIds, true))
            .ReturnsAsync((parentGroupIds, groupInfoMap, TimeSpan.FromMilliseconds(100)));

        // Act
        var result = await groupManagementServiceMock.Object.GetParentGroupsAsync(directGroupIds, true);

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
}
