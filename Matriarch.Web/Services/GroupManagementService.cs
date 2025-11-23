using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Matriarch.Web.Models;
using Matriarch.Web.Configuration;
using SharedSecurityGroup = Matriarch.Web.Models.SecurityGroup;
using SharedRoleAssignment = Matriarch.Web.Models.RoleAssignment;

namespace Matriarch.Web.Services;

public interface IGroupManagementService
{
    Task<List<string>> GetGroupMembershipsAsync(string principalId);
    Task<(List<string> allGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetAllGroupsAsync(List<string> directGroupIds);
    List<SharedSecurityGroup> BuildSecurityGroupsWithPreFetchedData(List<string> directGroupIds, Dictionary<string, GroupInfo> groupInfoMap, List<AzureRoleAssignmentDto> roleAssignments);
}

public class GroupInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> ParentGroupIds { get; set; } = new();
}

public class AzureRoleAssignmentDto
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string PrincipalType { get; set; } = string.Empty;
    public string RoleDefinitionId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}

public class GroupManagementService : IGroupManagementService
{
    private readonly ILogger<GroupManagementService> _logger;
    private readonly GraphServiceClient _graphClient;
    private const int MaxGraphPageSize = 999;

    public GroupManagementService(AppSettings settings, ILogger<GroupManagementService> logger)
    {
        _logger = logger;

        // Use ClientSecretCredential for authentication
        var credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(credential);
    }

    public async Task<List<string>> GetGroupMembershipsAsync(string principalId)
    {
        var groupIds = new List<string>();

        try
        {
            // Try as user first
            var memberOfPage = await _graphClient.Users[principalId].MemberOf.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group group && group.SecurityEnabled == true && !string.IsNullOrEmpty(group.Id))
                    {
                        groupIds.Add(group.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Not a user, trying as service principal");
            
            try
            {
                // Try as service principal
                var spMemberOfPage = await _graphClient.ServicePrincipals[principalId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                if (spMemberOfPage?.Value != null)
                {
                    foreach (var directoryObject in spMemberOfPage.Value)
                    {
                        if (directoryObject is Group group && group.SecurityEnabled == true && !string.IsNullOrEmpty(group.Id))
                        {
                            groupIds.Add(group.Id);
                        }
                    }
                }
            }
            catch (Exception spEx)
            {
                _logger.LogDebug(spEx, "Not a service principal, trying as group");
                
                try
                {
                    // Try as group - if the identity is a group, return it as its own "direct membership"
                    var group = await _graphClient.Groups[principalId].GetAsync();
                    if (group != null && group.SecurityEnabled == true)
                    {
                        // For a group, we treat it as being "member" of itself for role assignment purposes
                        groupIds.Add(principalId);
                        _logger.LogInformation("Identity is a group, will fetch its role assignments");
                    }
                }
                catch (Exception groupEx)
                {
                    _logger.LogWarning(groupEx, "Could not fetch group memberships for any entity type");
                }
            }
        }

        return groupIds;
    }

    public async Task<(List<string> allGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetAllGroupsAsync(List<string> directGroupIds)
    {
        var allGroupIds = new HashSet<string>(directGroupIds);
        var groupsToProcess = new Queue<string>(directGroupIds);
        var processedGroups = new HashSet<string>();
        var groupInfoMap = new Dictionary<string, GroupInfo>();

        while (groupsToProcess.Count > 0)
        {
            var currentGroupId = groupsToProcess.Dequeue();
            
            // Skip if already processed (circular reference protection)
            if (processedGroups.Contains(currentGroupId))
            {
                continue;
            }
            
            processedGroups.Add(currentGroupId);

            try
            {
                // Get group details and parent groups in a single operation
                var group = await _graphClient.Groups[currentGroupId].GetAsync();
                var memberOfPage = await _graphClient.Groups[currentGroupId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                var parentGroupIds = new List<string>();
                if (memberOfPage?.Value != null)
                {
                    foreach (var directoryObject in memberOfPage.Value)
                    {
                        if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true && !string.IsNullOrEmpty(parentGroup.Id))
                        {
                            parentGroupIds.Add(parentGroup.Id);
                            if (allGroupIds.Add(parentGroup.Id))
                            {
                                // New group found, add to queue for processing
                                groupsToProcess.Enqueue(parentGroup.Id);
                            }
                        }
                    }
                }

                // Store group information
                groupInfoMap[currentGroupId] = new GroupInfo
                {
                    Id = group?.Id ?? currentGroupId,
                    DisplayName = group?.DisplayName ?? string.Empty,
                    Description = group?.Description ?? string.Empty,
                    ParentGroupIds = parentGroupIds
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching parent groups for {GroupId}", currentGroupId);
            }
        }

        return (allGroupIds.ToList(), groupInfoMap);
    }

    public List<SharedSecurityGroup> BuildSecurityGroupsWithPreFetchedData(
        List<string> directGroupIds,
        Dictionary<string, GroupInfo> groupInfoMap,
        List<AzureRoleAssignmentDto> roleAssignments)
    {
        var securityGroups = new List<SharedSecurityGroup>();
        var processedGroups = new HashSet<string>();

        foreach (var groupId in directGroupIds)
        {
            var group = BuildSecurityGroupWithPreFetchedData(groupId, groupInfoMap, roleAssignments, processedGroups);
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return securityGroups;
    }

    private SharedSecurityGroup? BuildSecurityGroupWithPreFetchedData(
        string groupId,
        Dictionary<string, GroupInfo> groupInfoMap,
        List<AzureRoleAssignmentDto> allRoleAssignments,
        HashSet<string> processedGroups)
    {
        // Prevent infinite loops - if this group is already being processed, skip it
        if (processedGroups.Contains(groupId))
        {
            return null;
        }

        // Check if we have info for this group
        if (!groupInfoMap.TryGetValue(groupId, out var groupInfo))
        {
            _logger.LogWarning("Group info not found for {GroupId}", groupId);
            return null;
        }

        // Mark this group as being processed
        processedGroups.Add(groupId);

        // Get role assignments for this group
        var groupRoleAssignments = allRoleAssignments
            .Where(ra => ra.PrincipalId == groupId)
            .Select(ra => new SharedRoleAssignment
            {
                Id = ra.Id,
                RoleName = ra.RoleName,
                Scope = ra.Scope,
                AssignedTo = groupInfo.DisplayName
            })
            .ToList();

        // Build parent groups using pre-fetched data - simple matching with groupInfoMap
        var parentGroups = new List<SharedSecurityGroup>();
        foreach (var parentGroupId in groupInfo.ParentGroupIds)
        {
            var parentGroup = BuildSecurityGroupWithPreFetchedData(parentGroupId, groupInfoMap, allRoleAssignments, processedGroups);
            if (parentGroup != null)
            {
                parentGroups.Add(parentGroup);
            }
        }

        return new SharedSecurityGroup
        {
            Id = groupInfo.Id,
            DisplayName = groupInfo.DisplayName,
            Description = groupInfo.Description,
            RoleAssignments = groupRoleAssignments,
            ParentGroups = parentGroups
        };
    }
}
