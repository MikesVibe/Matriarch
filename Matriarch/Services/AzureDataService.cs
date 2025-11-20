using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Matriarch.Configuration;
using Matriarch.Models;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AzureRoleAssignment = Matriarch.Models.RoleAssignmentDto;

namespace Matriarch.Services;

public class AzureDataService
{
    private readonly ILogger<AzureDataService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly CachingService _cachingService;
    private readonly List<AzureRoleAssignment> _roleAssignments = [];
    private const string ResourceGraphApiEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";
    private const string _query = @"
                authorizationresources
                | where type =~ 'microsoft.authorization/roleassignments'
                | extend principalType = tostring(properties['principalType'])
                | extend principalId = tostring(properties['principalId'])
                | extend roleDefinitionId = tolower(tostring(properties['roleDefinitionId']))
                | extend scope = tostring(properties['scope'])
                | join kind=inner ( 
                    authorizationresources
                    | where type =~ 'microsoft.authorization/roledefinitions'
                    | extend id = tolower(id), roleName = tostring(properties['roleName'])
                ) on $left.roleDefinitionId == $right.id
                | project id, principalId, principalType, roleDefinitionId, roleName, scope";

    public AzureDataService(AppSettings settings, ILogger<AzureDataService> logger, CachingService cachingService)
    {
        _logger = logger;
        _cachingService = cachingService;

        // Use ClientSecretCredential for authentication
        _credential = new ClientSecretCredential(
            settings.Azure.TenantId,
            settings.Azure.ClientId,
            settings.Azure.ClientSecret);

        // Initialize Graph client for Entra ID operations
        _graphClient = new GraphServiceClient(_credential);

        // Initialize HttpClient for direct API calls
        _httpClient = new HttpClient();
    }

    public async Task<List<AzureRoleAssignment>> FetchRoleAssignmentsAsync()
    {
        const string cacheKey = "RoleAssignments";
        
        // Try to get cached data first
        var cachedData = await _cachingService.GetCachedDataAsync<List<AzureRoleAssignment>>(cacheKey);
        if (cachedData != null)
        {
            _logger.LogInformation($"Using cached role assignments ({cachedData.Count} items)");
            return cachedData;
        }

        _logger.LogInformation("Fetching role assignments from Azure Resource Graph API for all subscriptions...");

        try
        {
            var token = await GetAuthorizationToken();

            string? skipToken = null;
            int pageCount = 0;

            do
            {
                pageCount++;
                _logger.LogInformation("Fetching page {pageCount} of role assignments...", pageCount);

                var responseDocument = await FetchRoleAssignmentsPageData(token, skipToken);
                var pageOfRoleAssignments = ParseResponse(responseDocument).ToList();
                _roleAssignments.AddRange(pageOfRoleAssignments);

                skipToken = GetContinuationToken(responseDocument);

            } while (!string.IsNullOrEmpty(skipToken));

            _logger.LogInformation("Fetched {RoleAssignmentsCount} total role assignments from all accessible subscriptions across {PageCount} page(s)", _roleAssignments.Count, pageCount);

            await _cachingService.SetCachedDataAsync(cacheKey, _roleAssignments);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching role assignments from Resource Graph API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role assignments from Resource Graph API");
        }

        return _roleAssignments;
    }

    public async Task<List<EnterpriseApplicationDto>> FetchEnterpriseApplicationsAsync()
    {
        const string cacheKey = "EnterpriseApplications";
        
        // Try to get cached data first
        var cachedData = await _cachingService.GetCachedDataAsync<List<EnterpriseApplicationDto>>(cacheKey);
        if (cachedData != null)
        {
            _logger.LogInformation($"Using cached enterprise applications ({cachedData.Count} items)");
            return cachedData;
        }

        _logger.LogInformation("Fetching enterprise applications from Microsoft Graph...");
        var allEnterpriseApps = new List<EnterpriseApplicationDto>();

        try
        {
            var servicePrincipalsPage = await _graphClient.ServicePrincipals.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Top = 999; // Maximum page size for Graph API
            });

            if (servicePrincipalsPage is null)
            {
                _logger.LogWarning("No service principals found in the tenant.");
                return allEnterpriseApps;
            }

            int pageCount = 0;
            int processedCount = 0;

            var pageIterator = PageIterator<ServicePrincipal, ServicePrincipalCollectionResponse>
                .CreatePageIterator(
                    _graphClient,
                    servicePrincipalsPage,
                    sp =>
                    {
                        try
                        {
                            processedCount++;
                            var enterpriseApp = new EnterpriseApplicationDto
                            {
                                Id = sp.Id ?? string.Empty,
                                AppId = sp.AppId ?? string.Empty,
                                DisplayName = sp.DisplayName ?? string.Empty
                            };
                            allEnterpriseApps.Add(enterpriseApp);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error processing service principal {DisplayName}", sp.DisplayName);
                        }

                        return true; // Continue iterating
                    },
                    req =>
                    {
                        pageCount++;
                        _logger.LogInformation("Fetching page {PageCount} of service principals...", pageCount);
                        return req;
                    });

            await pageIterator.IterateAsync();

            _logger.LogInformation("Fetched {EnterpriseAppsCount} total enterprise applications across {PageCount} page(s)", allEnterpriseApps.Count, pageCount);

            // Note: Group memberships are not fetched here automatically
            // Call FetchGroupMembershipsForLinkedAppsAsync separately for specific apps

            // Cache the fetched data
            await _cachingService.SetCachedDataAsync(cacheKey, allEnterpriseApps);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching enterprise applications");
        }

        return allEnterpriseApps;
    }

    public async Task<List<AppRegistrationDto>> FetchAppRegistrationsAsync()
    {
        const string cacheKey = "AppRegistrations";
        
        // Try to get cached data first
        var cachedData = await _cachingService.GetCachedDataAsync<List<AppRegistrationDto>>(cacheKey);
        if (cachedData != null)
        {
            _logger.LogInformation($"Using cached app registrations ({cachedData.Count} items)");
            return cachedData;
        }

        _logger.LogInformation("Fetching app registrations from Microsoft Graph...");
        var appRegistrations = new List<AppRegistrationDto>();

        try
        {
            var applicationsPage = await _graphClient.Applications.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Expand = ["federatedIdentityCredentials"];
                requestConfiguration.QueryParameters.Top = 999; // Maximum page size for Graph API
            });

            if(applicationsPage is null)
            {
                _logger.LogWarning("No applications found in the tenant.");
                return appRegistrations;
            }

            int pageCount = 0;

            var pageIterator = PageIterator<Application, ApplicationCollectionResponse>
                .CreatePageIterator(
                    _graphClient,
                    applicationsPage,
                    app =>
                    {
                        var appReg = new AppRegistrationDto
                        {
                            Id = app.Id ?? string.Empty,
                            AppId = app.AppId ?? string.Empty,
                            DisplayName = app.DisplayName ?? string.Empty
                        };

                        appRegistrations.Add(appReg);
                        return true; // Continue iterating
                    },
                    req =>
                    {
                        pageCount++;
                        return req;
                    });

            await pageIterator.IterateAsync();

            _logger.LogInformation("Fetched {AppRegistrationsCount} total app registrations across {PageCount} page(s)", appRegistrations.Count, pageCount);
            
            // Cache the fetched data
            await _cachingService.SetCachedDataAsync(cacheKey, appRegistrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching app registrations");
        }

        return appRegistrations;
    }

    public async Task<List<SecurityGroupDto>> FetchSecurityGroupsAsync()
    {
        const string cacheKey = "SecurityGroups";

        // Try to get cached data first
        var cachedData = await _cachingService.GetCachedDataAsync<List<SecurityGroupDto>>(cacheKey);
        if (cachedData != null)
        {
            _logger.LogInformation($"Using cached security groups ({cachedData.Count} items)");
            return cachedData;
        }

        _logger.LogInformation("Fetching security groups from Microsoft Graph...");
        var securityGroups = new List<SecurityGroupDto>();

        try
        {
            var groupsPage = await _graphClient.Groups.GetAsync(config =>
            {
                config.QueryParameters.Filter = "securityEnabled eq true";
                config.QueryParameters.Top = 999; // Maximum page size for Graph API
            });

            if (groupsPage is null)
            {
                _logger.LogWarning("No security groups found in the tenant.");
                return securityGroups;
            }

            int pageCount = 0;
            int processedCount = 0;

            var pageIterator = PageIterator<Group, GroupCollectionResponse>
                .CreatePageIterator(
                    _graphClient,
                    groupsPage,
                    group =>
                    {
                        try
                        {
                            processedCount++;
                            var securityGroup = new SecurityGroupDto
                            {
                                Id = group.Id ?? string.Empty,
                                DisplayName = group.DisplayName ?? string.Empty,
                                Description = group.Description
                            };

                            securityGroups.Add(securityGroup);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error processing security group {DisplayName}", group.DisplayName);
                        }

                        return true; // Continue iterating
                    },
                    req =>
                    {
                        pageCount++;
                        _logger.LogInformation("Fetching page {PageCount} of security groups...", pageCount);
                        return req;
                    });

            await pageIterator.IterateAsync();

            _logger.LogInformation("Fetched {SecurityGroupsCount} total security groups across {PageCount} page(s)", 
                securityGroups.Count, pageCount);

            // Fetch members for all security groups
            //await FetchMembersForSecurityGroups(securityGroups);
            
            // Cache the fetched data
            await _cachingService.SetCachedDataAsync(cacheKey, securityGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching security groups");
        }

        return securityGroups;
    }

    private string? GetContinuationToken(JsonDocument responseDocument)
    {
        if (responseDocument.RootElement.TryGetProperty("$skipToken", out var skipTokenElement))
        {
            _logger.LogInformation($"More results available, continuing to next page...");
            return skipTokenElement.GetString();
        }

        return null;
    }

    public async Task FetchMembersForSecurityGroupsAsync(List<SecurityGroupDto> groups)
    {
        const string cacheKey = "SecurityGroupMembers";
        
        // Try to get cached data first
        var cachedMembers = await _cachingService.GetCachedDataAsync<Dictionary<string, List<GroupMemberDto>>>(cacheKey);
        
        if (cachedMembers != null)
        {
            _logger.LogInformation($"Using cached members for {cachedMembers.Count} security groups");
            
            // Apply cached members to the groups
            int matchedCount = 0;
            int cachedMemberCount = 0;
            foreach (var group in groups)
            {
                if (cachedMembers.TryGetValue(group.Id, out var members))
                {
                    group.Members = members;
                    matchedCount++;
                    cachedMemberCount += members.Count;
                }
                else
                {
                    group.Members = [];
                }
            }
            
            _logger.LogInformation($"Applied cached members to {matchedCount}/{groups.Count} security groups. Total members: {cachedMemberCount}");
            return;
        }

        _logger.LogInformation("Fetching members for {Count} security groups...", groups.Count);
        int processedCount = 0;
        int errorCount = 0;
        int totalMembers = 0;
        var membersDictionary = new Dictionary<string, List<GroupMemberDto>>();

        foreach (var group in groups)
        {
            try
            {
                processedCount++;
                var members = await FetchMembersForSingleGroupAsync(group.Id);
                
                group.Members = members;
                membersDictionary[group.Id] = members;
                totalMembers += members.Count;

                if (processedCount % 50 == 0)
                {
                    _logger.LogInformation("Fetched members for {ProcessedCount}/{TotalCount} security groups. Total members so far: {TotalMembers}",
                        processedCount, groups.Count, totalMembers);
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogWarning(ex, "Error fetching members for security group {DisplayName} (ID: {Id})",
                    group.DisplayName, group.Id);

                // Continue processing other groups even if one fails
                group.Members = [];
                membersDictionary[group.Id] = [];
            }
        }

        _logger.LogInformation("Completed fetching group members. Processed: {ProcessedCount}, Errors: {ErrorCount}, Total Members: {TotalMembers}",
            processedCount, errorCount, totalMembers);
        
        // Cache the fetched members
        await _cachingService.SetCachedDataAsync(cacheKey, membersDictionary);
    }

    private async Task<List<GroupMemberDto>> FetchMembersForSingleGroupAsync(string groupId)
    {
        var membersPage = await _graphClient.Groups[groupId].Members.GetAsync(config =>
        {
            config.QueryParameters.Top = 999;
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
        });

        if (membersPage is null)
        {
            return [];
        }

        var memberDtos = new List<GroupMemberDto>();

        // Use PageIterator to handle pagination for members
        var memberPageIterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
            .CreatePageIterator(
                _graphClient,
                membersPage,
                member =>
                {
                    if (!string.IsNullOrEmpty(member.Id))
                    {
                        var memberDto = new GroupMemberDto
                        {
                            Id = member.Id,
                            DisplayName = member.AdditionalData?.TryGetValue("displayName", out var displayName) == true 
                                ? displayName?.ToString() ?? string.Empty 
                                : string.Empty,
                            Type = DetermineMemberType(member),
                            UserPrincipalName = member.AdditionalData?.TryGetValue("userPrincipalName", out var upn) == true 
                                ? upn?.ToString() 
                                : null,
                            Mail = member.AdditionalData?.TryGetValue("mail", out var mail) == true 
                                ? mail?.ToString() 
                                : null
                        };
                        
                        memberDtos.Add(memberDto);
                    }
                    return true;
                });

        await memberPageIterator.IterateAsync();
        
        return memberDtos;
    }

    private static MemberType DetermineMemberType(DirectoryObject member)
    {
        // Check the actual type of the DirectoryObject
        return member switch
        {
            User => MemberType.User,
            Group => MemberType.Group,
            ServicePrincipal => MemberType.ServicePrincipal,
            Device => MemberType.Device,
            _ => MemberType.Unknown
        };
    }

    private async Task<AccessToken> GetAuthorizationToken()
    {
        var tokenRequestContext = new TokenRequestContext(["https://management.azure.com/.default"]);
        var token = await _credential.GetTokenAsync(tokenRequestContext, default);
        return token;
    }

    private async Task<JsonDocument> FetchRoleAssignmentsPageData(AccessToken token, string? skipToken)
    {
        // Create the request payload
        var requestBody = new Dictionary<string, object>
        {
            ["query"] = _query,
            ["options"] = new Dictionary<string, object>
            {
                ["resultFormat"] = "objectArray",
                ["$top"] = 1000  // Maximum page size
            }
        };

        // Add skipToken if we have one (for pagination)
        if (!string.IsNullOrEmpty(skipToken))
        {
            ((Dictionary<string, object>)requestBody["options"])["$skipToken"] = skipToken;
        }

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        // Set up the HTTP request
        var request = new HttpRequestMessage(HttpMethod.Post, ResourceGraphApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = content;

        // Execute the request
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseDocument = JsonDocument.Parse(responseContent);
        return responseDocument;
    }

    private static IEnumerable<AzureRoleAssignment> ParseResponse(JsonDocument responseDocument)
    {
        if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement))
        {
            yield break;
        }
        if (dataElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            yield return new AzureRoleAssignment
            {
                Id = row.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                PrincipalId = row.TryGetProperty("principalId", out var principalIdProp) ? principalIdProp.GetString() ?? string.Empty : string.Empty,
                PrincipalType = row.TryGetProperty("principalType", out var principalTypeProp) ? principalTypeProp.GetString() ?? string.Empty : string.Empty,
                RoleDefinitionId = row.TryGetProperty("roleDefinitionId", out var roleDefProp) ? roleDefProp.GetString() ?? string.Empty : string.Empty,
                RoleName = row.TryGetProperty("roleName", out var roleNameProp) ? roleNameProp.GetString() ?? string.Empty : string.Empty,
                Scope = row.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? string.Empty : string.Empty
            };
        }
    }

    public async Task FetchGroupMembershipsForLinkedAppsAsync(List<EnterpriseApplicationDto> apps)
    {
        const string cacheKey = "GroupMemberships";
        
        // Try to get cached data first
        var cachedMemberships = await _cachingService.GetCachedDataAsync<Dictionary<string, List<string>>>(cacheKey);
        
        if (cachedMemberships != null)
        {
            _logger.LogInformation($"Using cached group memberships for {cachedMemberships.Count} applications");
            
            // Apply cached memberships to the apps
            int matchedCount = 0;
            foreach (var app in apps)
            {
                if (cachedMemberships.TryGetValue(app.Id, out var memberships))
                {
                    app.GroupMemberships = memberships;
                    matchedCount++;
                }
                else
                {
                    app.GroupMemberships = [];
                }
            }
            
            _logger.LogInformation($"Applied cached group memberships to {matchedCount}/{apps.Count} applications");
            return;
        }

        _logger.LogInformation("Fetching group memberships for {Count} linked enterprise applications...", apps.Count);
        int processedCount = 0;
        int errorCount = 0;
        int totalMemberships = 0;
        var membershipsDictionary = new Dictionary<string, List<string>>();

        foreach (var app in apps)
        {
            try
            {
                processedCount++;
                var memberOfPage = await _graphClient.ServicePrincipals[app.Id].MemberOf.GetAsync(config =>
                {
                    config.QueryParameters.Top = 999;
                });

                if (memberOfPage is null)
                {
                    app.GroupMemberships = [];
                    membershipsDictionary[app.Id] = [];
                    continue;
                }

                var groupIds = new List<string>();

                // Use PageIterator to handle pagination for memberOf
                var memberOfPageIterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                    .CreatePageIterator(
                        _graphClient,
                        memberOfPage,
                        directoryObject =>
                        {
                            if (directoryObject is Group group && !string.IsNullOrEmpty(group.Id))
                            {
                                groupIds.Add(group.Id);
                                totalMemberships++;
                            }
                            return true;
                        });

                await memberOfPageIterator.IterateAsync();
                app.GroupMemberships = groupIds;
                membershipsDictionary[app.Id] = groupIds;

                if (processedCount % 50 == 0)
                {
                    _logger.LogInformation("Fetched group memberships for {ProcessedCount}/{TotalCount} applications. Total memberships so far: {TotalMemberships}", 
                        processedCount, apps.Count, totalMemberships);
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogWarning(ex, "Error fetching group memberships for {DisplayName} (ID: {Id})", 
                    app.DisplayName, app.Id);
                
                // Continue processing other apps even if one fails
                app.GroupMemberships = [];
                membershipsDictionary[app.Id] = [];
            }
        }

        _logger.LogInformation("Completed fetching group memberships. Processed: {ProcessedCount}, Errors: {ErrorCount}, Total Memberships: {TotalMemberships}", 
            processedCount, errorCount, totalMemberships);
        
        // Cache the fetched memberships
        await _cachingService.SetCachedDataAsync(cacheKey, membershipsDictionary);
    }
}
