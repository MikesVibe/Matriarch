using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Matriarch.Web.Models;
using Matriarch.Web.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Matriarch.Web.Services;

public interface IGroupManagementService
{
    Task<List<SecurityGroup>> GetGroupMembershipsAsync(Models.Identity identity);
    Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsAsync(List<string> directGroupIds);
    Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap, TimeSpan elapsedTime)> GetParentGroupsAsync(List<string> directGroupIds, bool useParallelProcessing);
    Task<List<SecurityGroup>> GetTransitiveGroupsAsync(List<SecurityGroup> directGroupIds, bool useParallelProcessing = false);
    List<SecurityGroup> BuildSecurityGroupsWithPreFetchedData(List<string> directGroupIds, Dictionary<string, GroupInfo> groupInfoMap, List<AzureRoleAssignmentDto> roleAssignments);
    Task<List<Models.Identity>> GetGroupMembersAsync(string groupId);
}

public class GroupInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> ParentGroupIds { get; set; } = new();
}

public class GroupManagementService : IGroupManagementService
{
    private readonly ILogger<GroupManagementService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly object _lock = new object();
    private GraphServiceClient? _graphClient;
    private string? _currentTenantId;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _transitiveGroupSemaphore;
    private const int MaxGraphPageSize = 999;

    public GroupManagementService(AppSettings settings, ITenantContext tenantContext, ILogger<GroupManagementService> logger)
    {
        _logger = logger;
        _settings = settings;
        _tenantContext = tenantContext;
        
        // Initialize semaphore for rate limiting transitive group requests
        _transitiveGroupSemaphore = new SemaphoreSlim(
            settings.Parallelization.MaxConcurrentTransitiveGroupRequests,
            settings.Parallelization.MaxConcurrentTransitiveGroupRequests);
    }

    private GraphServiceClient GetGraphClient()
    {
        var tenantSettings = _tenantContext.GetCurrentTenantSettings();
        
        lock (_lock)
        {
            // Recreate client if tenant has changed
            if (_graphClient == null || _currentTenantId != tenantSettings.TenantId)
            {
                var credential = new ClientSecretCredential(
                    tenantSettings.TenantId,
                    tenantSettings.ClientId,
                    tenantSettings.ClientSecret);

                _graphClient = new GraphServiceClient(credential);
                _currentTenantId = tenantSettings.TenantId;
            }

            return _graphClient;
        }
    }

    public async Task<List<SecurityGroup>> GetGroupMembershipsAsync(Models.Identity identity)
    {
        var groupIds = new List<SecurityGroup>();

        try
        {
            DirectoryObjectCollectionResponse? memberOfPage = identity.Type switch
            {
                IdentityType.User => await GetGraphClient().Users[identity.ObjectId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                }),
                IdentityType.ServicePrincipal or 
                IdentityType.UserAssignedManagedIdentity or 
                IdentityType.SystemAssignedManagedIdentity => await GetGraphClient().ServicePrincipals[identity.ObjectId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                }),
                IdentityType.Group => await HandleGroupIdentityAsync(identity.ObjectId, groupIds),
                _ => throw new InvalidOperationException($"Unsupported identity type: {identity.Type}")
            };

            if (memberOfPage?.Value != null)
            {
                foreach (var directoryObject in memberOfPage.Value)
                {
                    if (directoryObject is Group group && group.SecurityEnabled == true && !string.IsNullOrEmpty(group.Id))
                    {
                        groupIds.Add(new SecurityGroup() { Id = group.Id, DisplayName = group?.DisplayName ?? ""});
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching group memberships for identity {ObjectId} of type {IdentityType}", 
                identity.ObjectId, identity.Type);
            throw;
        }

        return groupIds;
    }

    private async Task<DirectoryObjectCollectionResponse?> HandleGroupIdentityAsync(string groupId, List<SecurityGroup> groups)
    {
        // For a group identity, verify it exists and is a security group
        var group = await GetGraphClient().Groups[groupId].GetAsync();
        if (group != null && group.SecurityEnabled == true)
        {
            // For a group, we treat it as being "member" of itself for role assignment purposes
            groups.Add(new SecurityGroup() { Id = group?.Id ?? "", DisplayName = group?.DisplayName ?? ""});
            _logger.LogInformation("Identity is a group, will fetch its role assignments");
        }
        // Return null as groups don't have memberOf in this context
        return null;
    }

    public async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsAsync(List<string> directGroupIds)
    {
        var result = await GetParentGroupsAsync(directGroupIds, false);
        return (result.parentGroupIds, result.groupInfoMap);
    }

    public async Task<List<SecurityGroup>> GetTransitiveGroupsAsync(List<SecurityGroup> directGroupIds, bool useParallelProcessing = false)
    {
        if (useParallelProcessing)
        {
            return await GetTransitiveGroupsParallelAsync(directGroupIds);
        }
        else
        {
            return await GetTransitiveGroupsSequentialAsync(directGroupIds);
        }
    }

    private async Task<List<SecurityGroup>> GetTransitiveGroupsSequentialAsync(List<SecurityGroup> directGroupIds)
    {
        _logger.LogInformation("Fetching transitive groups for {Count} direct groups sequentially", directGroupIds.Count);
        
        var result = new List<SecurityGroup>();

        foreach (var group in directGroupIds)
        {
            try
            {
                _logger.LogDebug("Fetching transitive members for group {GroupId}", group.Id);
                
                var transitiveMemberOfPage = await GetGraphClient().Groups[group.Id].TransitiveMemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                if (transitiveMemberOfPage?.Value != null)
                {
                    foreach (var directoryObject in transitiveMemberOfPage.Value)
                    {
                        if (directoryObject is Group parentGroup && 
                            parentGroup.SecurityEnabled == true && 
                            !string.IsNullOrEmpty(parentGroup.Id))
                        {
                            result.Add(new SecurityGroup() { Id = parentGroup.Id, DisplayName = parentGroup?.DisplayName ?? "" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching transitive groups for {GroupId}", group.Id);
            }
        }

        _logger.LogInformation("Completed fetching transitive groups sequentially: found {Count} total groups from {InputCount} direct groups", 
            result.Count, directGroupIds.Count);
        
        return result;
    }

    private async Task<List<SecurityGroup>> GetTransitiveGroupsParallelAsync(List<SecurityGroup> directGroupIds)
    {
        _logger.LogInformation("Fetching transitive groups for {Count} direct groups with parallel processing and rate limiting", directGroupIds.Count);
        
        var result = new ConcurrentBag<SecurityGroup>();
        var batchSize = _settings.Parallelization.TransitiveGroupBatchSize;
        var delayBetweenBatches = _settings.Parallelization.DelayBetweenBatchesMilliseconds;

        // Process groups in batches to manage throttling
        var batches = directGroupIds
            .Select((group, index) => new { group, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.group).ToList())
            .ToList();

        _logger.LogInformation("Processing {BatchCount} batches of transitive groups (batch size: {BatchSize})", 
            batches.Count, batchSize);

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = batches[batchIndex];
            _logger.LogDebug("Processing batch {BatchIndex}/{TotalBatches} with {Count} groups", 
                batchIndex + 1, batches.Count, batch.Count);

            // Process batch in parallel with semaphore limiting
            var batchTasks = batch.Select(async group =>
            {
                await _transitiveGroupSemaphore.WaitAsync();
                try
                {
                    await FetchTransitiveGroupsForSingleGroupAsync(group, result);
                }
                finally
                {
                    _transitiveGroupSemaphore.Release();
                }
            });

            await Task.WhenAll(batchTasks);

            // Add delay between batches to avoid throttling (except after the last batch)
            if (batchIndex < batches.Count - 1 && delayBetweenBatches > 0)
            {
                _logger.LogDebug("Waiting {Delay}ms before next batch", delayBetweenBatches);
                await Task.Delay(delayBetweenBatches);
            }
        }

        var resultList = result.ToList();
        _logger.LogInformation("Completed fetching transitive groups in parallel: found {Count} total transitive groups from {InputCount} direct groups", 
            resultList.Count, directGroupIds.Count);
        
        return resultList;
    }

    private async Task FetchTransitiveGroupsForSingleGroupAsync(SecurityGroup group, ConcurrentBag<SecurityGroup> result)
    {
        var groupId = group.Id;
        try
        {
            _logger.LogDebug("Fetching transitive members for group {GroupId}", groupId);
            
            // Use TransitiveMemberOf to get all parent groups (direct and indirect)
            var transitiveMemberOfPage = await GetGraphClient().Groups[groupId].TransitiveMemberOf.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (transitiveMemberOfPage?.Value != null)
            {
                foreach (var directoryObject in transitiveMemberOfPage.Value)
                {
                    if (directoryObject is Group parentGroup && 
                        parentGroup.SecurityEnabled == true && 
                        !string.IsNullOrEmpty(parentGroup.Id))
                    {
                        result.Add(new SecurityGroup() { Id = parentGroup.Id, DisplayName = parentGroup?.DisplayName ?? "", ChildGroup = group });
                    }
                }
            }
        }
        catch (Azure.RequestFailedException azEx) when (azEx.Status == 429 || azEx.Status == 503)
        {
            _logger.LogWarning("Azure throttling detected (Status: {Status}) for group {GroupId}, skipping this group", 
                azEx.Status, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching transitive groups for {GroupId}", groupId);
        }
    }

    public async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap, TimeSpan elapsedTime)> GetParentGroupsAsync(List<string> directGroupIds, bool useParallelProcessing)
    {
        var stopwatch = Stopwatch.StartNew();

        var (parentGroupIds, groupInfoMap) = await GetParentGroupsSequentialAsync(directGroupIds);
        stopwatch.Stop();
        _logger.LogInformation("GetParentGroupsAsync (Sequential) completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return (parentGroupIds, groupInfoMap, stopwatch.Elapsed);
    }

    private async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsSequentialAsync(List<string> directGroupIds)
    {
        var allGroupIds = new HashSet<string>(directGroupIds);
        var groupInfoMap = new Dictionary<string, GroupInfo>();

        // Process each direct group to get all transitive parent groups
        foreach (var groupId in directGroupIds)
        {
            try
            {
                // Get group details
                var group = await GetGraphClient().Groups[groupId].GetAsync();
                
                // Use TransitiveMemberOf to get all parent groups (direct and indirect) in one call
                var transitiveMemberOfPage = await GetGraphClient().Groups[groupId].TransitiveMemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                var directParentGroupIds = new List<string>();
                
                // First, get direct parent groups using MemberOf
                var memberOfPage = await GetGraphClient().Groups[groupId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });
                
                if (memberOfPage?.Value != null)
                {
                    foreach (var directoryObject in memberOfPage.Value)
                    {
                        if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true && !string.IsNullOrEmpty(parentGroup.Id))
                        {
                            directParentGroupIds.Add(parentGroup.Id);
                        }
                    }
                }

                // Store group information with direct parents
                groupInfoMap[groupId] = new GroupInfo
                {
                    Id = group?.Id ?? groupId,
                    DisplayName = group?.DisplayName ?? string.Empty,
                    Description = group?.Description ?? string.Empty,
                    ParentGroupIds = directParentGroupIds
                };

                // Add all transitive parent groups to the set
                if (transitiveMemberOfPage?.Value != null)
                {
                    foreach (var directoryObject in transitiveMemberOfPage.Value)
                    {
                        if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true && !string.IsNullOrEmpty(parentGroup.Id))
                        {
                            allGroupIds.Add(parentGroup.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching parent groups for {GroupId}", groupId);
            }
        }

        // Now fetch details for all discovered parent groups
        var parentGroupIds = allGroupIds.Except(directGroupIds).ToList();
        foreach (var parentGroupId in parentGroupIds)
        {
            if (groupInfoMap.ContainsKey(parentGroupId))
            {
                continue; // Already have info for this group
            }

            try
            {
                var group = await GetGraphClient().Groups[parentGroupId].GetAsync();
                var memberOfPage = await GetGraphClient().Groups[parentGroupId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                var directParentGroupIds = new List<string>();
                if (memberOfPage?.Value != null)
                {
                    foreach (var directoryObject in memberOfPage.Value)
                    {
                        if (directoryObject is Group parentGroup && parentGroup.SecurityEnabled == true && !string.IsNullOrEmpty(parentGroup.Id))
                        {
                            directParentGroupIds.Add(parentGroup.Id);
                        }
                    }
                }

                groupInfoMap[parentGroupId] = new GroupInfo
                {
                    Id = group?.Id ?? parentGroupId,
                    DisplayName = group?.DisplayName ?? string.Empty,
                    Description = group?.Description ?? string.Empty,
                    ParentGroupIds = directParentGroupIds
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching parent group details for {GroupId}", parentGroupId);
            }
        }

        return (parentGroupIds, groupInfoMap);
    }

    public List<SecurityGroup> BuildSecurityGroupsWithPreFetchedData(
        List<string> groupIds,
        Dictionary<string, GroupInfo> groupInfoMap,
        List<AzureRoleAssignmentDto> roleAssignments)
    {
        var securityGroups = new List<SecurityGroup>();

        foreach (var groupId in groupIds)
        {
            var group = BuildSecurityGroupWithPreFetchedDataMiki(groupId, groupInfoMap, roleAssignments);
            if (group != null)
            {
                securityGroups.Add(group);
            }
        }

        return securityGroups;
    }

    private SecurityGroup? BuildSecurityGroupWithPreFetchedDataMiki(
      string groupId,
      Dictionary<string, GroupInfo> groupInfoMap,
      List<AzureRoleAssignmentDto> allRoleAssignments)
    {
        // Check if we have info for this group
        if (!groupInfoMap.TryGetValue(groupId, out var groupInfo))
        {
            _logger.LogWarning("Group info not found for {GroupId}", groupId);
            return null;
        }

        // Get role assignments for this group
        var groupRoleAssignments = allRoleAssignments
            .Where(ra => ra.PrincipalId == groupId)
            .Select(ra => new Models.RoleAssignment
            {
                Id = ra.Id,
                RoleName = ra.RoleName,
                Scope = ra.Scope,
                AssignedTo = groupInfo.DisplayName
            })
            .ToList();

        return new SecurityGroup
        {
            Id = groupInfo.Id,
            DisplayName = groupInfo.DisplayName,
            Description = groupInfo.Description,
            RoleAssignments = groupRoleAssignments,
        };
    }

    public async Task<List<Models.Identity>> GetGroupMembersAsync(string groupId)
    {
        _logger.LogInformation("Fetching members for group {GroupId}", groupId);
        var members = new List<Models.Identity>();

        try
        {
            // Get users
            var membersPage = await GetGraphClient().Groups[groupId].Members.GraphUser.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (membersPage?.Value != null)
            {
                foreach (var user in membersPage.Value)
                {
                    members.Add(new Models.Identity
                    {
                        ObjectId = user.Id ?? "",
                        ApplicationId = "",
                        Email = user.Mail ?? user.UserPrincipalName ?? "",
                        Name = user.DisplayName ?? "",
                        Type = IdentityType.User
                    });
                }
            }

            // Get service principals
            var spMembersPage = await GetGraphClient().Groups[groupId].Members.GraphServicePrincipal.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (spMembersPage?.Value != null)
            {
                foreach (var sp in spMembersPage.Value)
                {
                    var spType = sp.ServicePrincipalType?.ToLower() == "managedidentity" 
                        ? IdentityType.UserAssignedManagedIdentity 
                        : IdentityType.ServicePrincipal;
                    
                    members.Add(new Models.Identity
                    {
                        ObjectId = sp.Id ?? "",
                        ApplicationId = sp.AppId ?? "",
                        Email = "",
                        Name = sp.DisplayName ?? "",
                        Type = spType,
                        ServicePrincipalType = sp.ServicePrincipalType
                    });
                }
            }

            // Get groups
            var groupMembersPage = await GetGraphClient().Groups[groupId].Members.GraphGroup.GetAsync(config =>
            {
                config.QueryParameters.Top = MaxGraphPageSize;
            });

            if (groupMembersPage?.Value != null)
            {
                foreach (var group in groupMembersPage.Value)
                {
                    if (group.SecurityEnabled == true)
                    {
                        members.Add(new Models.Identity
                        {
                            ObjectId = group.Id ?? "",
                            ApplicationId = "",
                            Email = "",
                            Name = group.DisplayName ?? "",
                            Type = IdentityType.Group
                        });
                    }
                }
            }

            _logger.LogInformation("Found {Count} members in group {GroupId}", members.Count, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching members for group {GroupId}", groupId);
        }

        return members;
    }
}
