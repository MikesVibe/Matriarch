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
    Task<List<string>> GetGroupMembershipsAsync(Models.Identity identity);
    Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsAsync(List<string> directGroupIds);
    Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap, TimeSpan elapsedTime)> GetParentGroupsAsync(List<string> directGroupIds, bool useParallelProcessing);
    Task<Dictionary<string, List<string>>> GetTransitiveGroupsAsync(List<string> directGroupIds);
    List<SecurityGroup> BuildSecurityGroupsWithPreFetchedData(List<string> directGroupIds, Dictionary<string, GroupInfo> groupInfoMap, List<AzureRoleAssignmentDto> roleAssignments);
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
    private readonly AppSettings _settings;
    private const int MaxGraphPageSize = 999;

    public GroupManagementService(AppSettings settings, ILogger<GroupManagementService> logger)
    {
        _logger = logger;
        _settings = settings;

        // Use ClientSecretCredential for authentication
        var credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(credential);
    }

    public async Task<List<string>> GetGroupMembershipsAsync(Models.Identity identity)
    {
        var groupIds = new List<string>();

        try
        {
            DirectoryObjectCollectionResponse? memberOfPage = identity.Type switch
            {
                IdentityType.User => await _graphClient.Users[identity.ObjectId].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                }),
                IdentityType.ServicePrincipal or 
                IdentityType.UserAssignedManagedIdentity or 
                IdentityType.SystemAssignedManagedIdentity => await _graphClient.ServicePrincipals[identity.ObjectId].MemberOf.GetAsync(config =>
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
                        groupIds.Add(group.Id);
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

    private async Task<DirectoryObjectCollectionResponse?> HandleGroupIdentityAsync(string groupId, List<string> groupIds)
    {
        // For a group identity, verify it exists and is a security group
        var group = await _graphClient.Groups[groupId].GetAsync();
        if (group != null && group.SecurityEnabled == true)
        {
            // For a group, we treat it as being "member" of itself for role assignment purposes
            groupIds.Add(groupId);
            _logger.LogInformation("Identity is a group, will fetch its role assignments");
        }
        // Return null as groups don't have memberOf in this context
        return null;
    }

    public async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsAsync(List<string> directGroupIds)
    {
        var result = await GetParentGroupsAsync(directGroupIds, _settings.Parallelization.EnableParallelProcessing);
        return (result.parentGroupIds, result.groupInfoMap);
    }

    public async Task<Dictionary<string, List<string>>> GetTransitiveGroupsAsync(List<string> directGroupIds)
    {
        _logger.LogInformation("Fetching transitive groups for {Count} direct groups", directGroupIds.Count);
        
        var result = new Dictionary<string, List<string>>();

        foreach (var groupId in directGroupIds)
        {
            var transitiveGroupIds = new List<string>();
            
            try
            {
                _logger.LogDebug("Fetching transitive members for group {GroupId}", groupId);
                
                // Use TransitiveMemberOf to get all parent groups (direct and indirect)
                var transitiveMemberOfPage = await _graphClient.Groups[groupId].TransitiveMemberOf.GetAsync(config =>
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
                            transitiveGroupIds.Add(parentGroup.Id);
                        }
                    }
                }

                result[groupId] = transitiveGroupIds;
                _logger.LogDebug("Found {Count} transitive groups for group {GroupId}", transitiveGroupIds.Count, groupId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching transitive groups for {GroupId}", groupId);
                result[groupId] = transitiveGroupIds; // Add empty list on error
            }
        }

        _logger.LogInformation("Completed fetching transitive groups for {Count} direct groups", directGroupIds.Count);
        return result;
    }

    public async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap, TimeSpan elapsedTime)> GetParentGroupsAsync(List<string> directGroupIds, bool useParallelProcessing)
    {
        var stopwatch = Stopwatch.StartNew();
        
        if (useParallelProcessing)
        {
            var (parentGroupIds, groupInfoMap) = await GetParentGroupsParallelAsync(directGroupIds);
            stopwatch.Stop();
            _logger.LogInformation("GetParentGroupsAsync (Parallel) completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return (parentGroupIds, groupInfoMap, stopwatch.Elapsed);
        }
        else
        {
            var (parentGroupIds, groupInfoMap) = await GetParentGroupsSequentialAsync(directGroupIds);
            stopwatch.Stop();
            _logger.LogInformation("GetParentGroupsAsync (Sequential) completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return (parentGroupIds, groupInfoMap, stopwatch.Elapsed);
        }
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
                var group = await _graphClient.Groups[groupId].GetAsync();
                
                // Use TransitiveMemberOf to get all parent groups (direct and indirect) in one call
                var transitiveMemberOfPage = await _graphClient.Groups[groupId].TransitiveMemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = MaxGraphPageSize;
                });

                var directParentGroupIds = new List<string>();
                
                // First, get direct parent groups using MemberOf
                var memberOfPage = await _graphClient.Groups[groupId].MemberOf.GetAsync(config =>
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
                var group = await _graphClient.Groups[parentGroupId].GetAsync();
                var memberOfPage = await _graphClient.Groups[parentGroupId].MemberOf.GetAsync(config =>
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

    private async Task<(List<string> parentGroupIds, Dictionary<string, GroupInfo> groupInfoMap)> GetParentGroupsParallelAsync(List<string> directGroupIds)
    {
        var allGroupIds = new ConcurrentBag<string>(directGroupIds);
        var groupsToProcess = new ConcurrentQueue<string>(directGroupIds);
        var processedGroups = new ConcurrentDictionary<string, byte>();
        var groupInfoMap = new ConcurrentDictionary<string, GroupInfo>();
        
        var maxDegreeOfParallelism = _settings.Parallelization.MaxDegreeOfParallelism;
        var processingTasks = new List<Task>();

        // Create parallel workers
        for (int i = 0; i < maxDegreeOfParallelism; i++)
        {
            processingTasks.Add(Task.Run(async () =>
            {
                while (groupsToProcess.TryDequeue(out var currentGroupId))
                {
                    // Skip if already processed (circular reference protection)
                    if (!processedGroups.TryAdd(currentGroupId, 0))
                    {
                        continue;
                    }

                    try
                    {
                        var groupInfo = await FetchGroupInfoWithRetryAsync(currentGroupId);
                        
                        if (groupInfo != null)
                        {
                            groupInfoMap[currentGroupId] = groupInfo;
                            
                            // Add new parent groups to the queue
                            foreach (var parentGroupId in groupInfo.ParentGroupIds)
                            {
                                // Check if not already processed or in queue
                                if (!processedGroups.ContainsKey(parentGroupId))
                                {
                                    allGroupIds.Add(parentGroupId);
                                    groupsToProcess.Enqueue(parentGroupId);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error fetching parent groups for {GroupId}", currentGroupId);
                    }
                }
            }));
        }

        // Wait for all workers to complete
        await Task.WhenAll(processingTasks);

        var allGroupIdsList = allGroupIds.Distinct().ToList();
        return (allGroupIdsList.Except(directGroupIds).ToList(), groupInfoMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    private async Task<GroupInfo?> FetchGroupInfoWithRetryAsync(string groupId)
    {
        var maxRetries = _settings.Parallelization.MaxRetryAttempts;
        var retryDelay = _settings.Parallelization.RetryDelayMilliseconds;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Get group details and parent groups in a single operation
                var group = await _graphClient.Groups[groupId].GetAsync();
                var memberOfPage = await _graphClient.Groups[groupId].MemberOf.GetAsync(config =>
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
                        }
                    }
                }

                return new GroupInfo
                {
                    Id = group?.Id ?? groupId,
                    DisplayName = group?.DisplayName ?? string.Empty,
                    Description = group?.Description ?? string.Empty,
                    ParentGroupIds = parentGroupIds
                };
            }
            catch (Azure.RequestFailedException azEx) when (azEx.Status == 429 || azEx.Status == 503)
            {
                // Azure SDK throttling or service unavailable
                if (attempt < maxRetries)
                {
                    var delay = retryDelay * (int)Math.Pow(2, attempt); // Exponential backoff
                    _logger.LogWarning("Azure throttling detected (Status: {Status}) for {GroupId}, retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})", 
                        azEx.Status, groupId, delay, attempt + 1, maxRetries);
                    await Task.Delay(delay);
                }
                else
                {
                    _logger.LogError(azEx, "Max retries exceeded for {GroupId} due to throttling", groupId);
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Check for throttling in exception message as fallback
                bool isPotentialThrottling = ex.Message.Contains("429") || ex.Message.Contains("503") || 
                                           ex.Message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) || 
                                           ex.Message.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase);
                
                if (isPotentialThrottling && attempt < maxRetries)
                {
                    var delay = retryDelay * (int)Math.Pow(2, attempt); // Exponential backoff
                    _logger.LogWarning("Potential throttling detected for {GroupId}, retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})", 
                        groupId, delay, attempt + 1, maxRetries);
                    await Task.Delay(delay);
                }
                else if (attempt < maxRetries)
                {
                    _logger.LogWarning(ex, "Error fetching group info for {GroupId} on attempt {Attempt}, retrying...", 
                        groupId, attempt + 1);
                    await Task.Delay(retryDelay);
                }
                else
                {
                    _logger.LogError(ex, "Max retries exceeded for {GroupId}", groupId);
                    throw;
                }
            }
        }

        return null;
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
}
