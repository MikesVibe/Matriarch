using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Matriarch.Configuration;
using Matriarch.Models;

namespace Matriarch.Services;

public class Neo4jService : IAsyncDisposable
{
    private readonly ILogger<Neo4jService> _logger;
    private readonly IDriver _driver;

    public Neo4jService(AppSettings settings, ILogger<Neo4jService> logger)
    {
        _logger = logger;
        _driver = GraphDatabase.Driver(
            settings.Neo4j.Uri,
            AuthTokens.Basic(settings.Neo4j.Username, settings.Neo4j.Password));
    }

    public async Task InitializeDatabaseAsync()
    {
        _logger.LogInformation("Initializing Neo4j database schema...");
        
        await using var session = _driver.AsyncSession();
        
        try
        {
            // Create constraints and indexes
            var constraints = new[]
            {
                "CREATE CONSTRAINT IF NOT EXISTS FOR (a:AppRegistration) REQUIRE a.id IS UNIQUE",
                "CREATE CONSTRAINT IF NOT EXISTS FOR (e:EnterpriseApp) REQUIRE e.id IS UNIQUE",
                "CREATE CONSTRAINT IF NOT EXISTS FOR (g:SecurityGroup) REQUIRE g.id IS UNIQUE",
                "CREATE CONSTRAINT IF NOT EXISTS FOR (r:RoleAssignment) REQUIRE r.id IS UNIQUE",
                "CREATE CONSTRAINT IF NOT EXISTS FOR (f:FederatedCredential) REQUIRE f.id IS UNIQUE"
            };

            foreach (var constraint in constraints)
            {
                await session.RunAsync(constraint);
            }

            _logger.LogInformation("Database schema initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database schema");
            throw;
        }
    }

    public async Task StoreAppRegistrationsAsync(List<AppRegistration> appRegistrations)
    {
        _logger.LogInformation($"Storing {appRegistrations.Count} app registrations in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        
        foreach (var appReg in appRegistrations)
        {
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    // Create AppRegistration node
                    await tx.RunAsync(@"
                        MERGE (a:AppRegistration {id: $id})
                        SET a.appId = $appId,
                            a.displayName = $displayName
                    ", new
                    {
                        id = appReg.Id,
                        appId = appReg.AppId,
                        displayName = appReg.DisplayName
                    });

                    // Create FederatedCredential nodes and relationships
                    foreach (var fedCred in appReg.FederatedCredentials)
                    {
                        await tx.RunAsync(@"
                            MATCH (a:AppRegistration {id: $appRegId})
                            MERGE (f:FederatedCredential {id: $id})
                            SET f.name = $name,
                                f.issuer = $issuer,
                                f.subject = $subject,
                                f.audiences = $audiences
                            MERGE (a)-[:HAS_FEDERATED_CREDENTIAL]->(f)
                        ", new
                        {
                            appRegId = appReg.Id,
                            id = fedCred.Id,
                            name = fedCred.Name,
                            issuer = fedCred.Issuer,
                            subject = fedCred.Subject,
                            audiences = fedCred.Audiences
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing app registration {appReg.DisplayName}");
            }
        }

        _logger.LogInformation("App registrations stored successfully");
    }

    public async Task StoreEnterpriseApplicationsAsync(List<EnterpriseApplication> enterpriseApps)
    {
        _logger.LogInformation($"Storing {enterpriseApps.Count} enterprise applications in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        
        foreach (var app in enterpriseApps)
        {
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    // Create EnterpriseApp node
                    await tx.RunAsync(@"
                        MERGE (e:EnterpriseApp {id: $id})
                        SET e.appId = $appId,
                            e.displayName = $displayName
                    ", new
                    {
                        id = app.Id,
                        appId = app.AppId,
                        displayName = app.DisplayName
                    });

                    // Link to AppRegistration by AppId
                    await tx.RunAsync(@"
                        MATCH (e:EnterpriseApp {id: $enterpriseAppId})
                        MATCH (a:AppRegistration {appId: $appId})
                        MERGE (a)-[:HAS_SERVICE_PRINCIPAL]->(e)
                    ", new
                    {
                        enterpriseAppId = app.Id,
                        appId = app.AppId
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing enterprise application {app.DisplayName}");
            }
        }

        _logger.LogInformation("Enterprise applications stored successfully");
    }

    public async Task StoreSecurityGroupsAsync(List<SecurityGroup> securityGroups)
    {
        _logger.LogInformation($"Storing {securityGroups.Count} security groups in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        
        foreach (var group in securityGroups)
        {
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync(@"
                        MERGE (g:SecurityGroup {id: $id})
                        SET g.displayName = $displayName,
                            g.description = $description
                    ", new
                    {
                        id = group.Id,
                        displayName = group.DisplayName,
                        description = group.Description ?? string.Empty
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing security group {group.DisplayName}");
            }
        }

        _logger.LogInformation("Security groups stored successfully");
    }

    public async Task StoreRoleAssignmentsAsync(List<RoleAssignment> roleAssignments, 
        List<EnterpriseApplication> enterpriseApps, 
        List<SecurityGroup> securityGroups)
    {
        _logger.LogInformation($"Storing {roleAssignments.Count} role assignments in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        
        // Create dictionaries for quick lookup
        var enterpriseAppDict = enterpriseApps.ToDictionary(e => e.Id, e => e);
        var securityGroupDict = securityGroups.ToDictionary(g => g.Id, g => g);
        
        foreach (var roleAssignment in roleAssignments)
        {
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    // Create RoleAssignment node
                    await tx.RunAsync(@"
                        MERGE (r:RoleAssignment {id: $id})
                        SET r.principalId = $principalId,
                            r.principalType = $principalType,
                            r.roleDefinitionId = $roleDefinitionId,
                            r.roleName = $roleName,
                            r.scope = $scope
                    ", new
                    {
                        id = roleAssignment.Id,
                        principalId = roleAssignment.PrincipalId,
                        principalType = roleAssignment.PrincipalType,
                        roleDefinitionId = roleAssignment.RoleDefinitionId,
                        roleName = roleAssignment.RoleName,
                        scope = roleAssignment.Scope
                    });

                    // Link to EnterpriseApp if the principal is a service principal
                    if (enterpriseAppDict.ContainsKey(roleAssignment.PrincipalId))
                    {
                        await tx.RunAsync(@"
                            MATCH (e:EnterpriseApp {id: $principalId})
                            MATCH (r:RoleAssignment {id: $roleAssignmentId})
                            MERGE (e)-[:HAS_ROLE_ASSIGNMENT]->(r)
                        ", new
                        {
                            principalId = roleAssignment.PrincipalId,
                            roleAssignmentId = roleAssignment.Id
                        });
                    }

                    // Link to SecurityGroup if the principal is a group
                    if (securityGroupDict.ContainsKey(roleAssignment.PrincipalId))
                    {
                        await tx.RunAsync(@"
                            MATCH (g:SecurityGroup {id: $principalId})
                            MATCH (r:RoleAssignment {id: $roleAssignmentId})
                            MERGE (g)-[:HAS_ROLE_ASSIGNMENT]->(r)
                        ", new
                        {
                            principalId = roleAssignment.PrincipalId,
                            roleAssignmentId = roleAssignment.Id
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing role assignment {roleAssignment.Id}");
            }
        }

        _logger.LogInformation("Role assignments stored successfully");
    }

    public async Task StoreGroupMembershipsAsync(List<EnterpriseApplication> enterpriseApps)
    {
        _logger.LogInformation("Storing group memberships in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        
        foreach (var app in enterpriseApps)
        {
            if (app.GroupMemberships.Count == 0) continue;
            
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    foreach (var groupId in app.GroupMemberships)
                    {
                        await tx.RunAsync(@"
                            MATCH (e:EnterpriseApp {id: $enterpriseAppId})
                            MATCH (g:SecurityGroup {id: $groupId})
                            MERGE (e)-[:MEMBER_OF]->(g)
                        ", new
                        {
                            enterpriseAppId = app.Id,
                            groupId
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing group memberships for {app.DisplayName}");
            }
        }

        _logger.LogInformation("Group memberships stored successfully");
    }

    public async Task StoreSecurityGroupMembersAsync(List<SecurityGroup> securityGroups)
    {
        _logger.LogInformation("Storing security group members in Neo4j...");
        
        await using var session = _driver.AsyncSession();
        int totalRelationships = 0;
        
        foreach (var group in securityGroups)
        {
            if (group.Members.Count == 0) continue;
            
            try
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    foreach (var memberId in group.Members)
                    {
                        // Try to link to EnterpriseApp
                        var result = await tx.RunAsync(@"
                            MATCH (g:SecurityGroup {id: $groupId})
                            OPTIONAL MATCH (e:EnterpriseApp {id: $memberId})
                            OPTIONAL MATCH (u:User {id: $memberId})
                            OPTIONAL MATCH (nested:SecurityGroup {id: $memberId})
                            WITH g, e, u, nested, $memberId as memberId
                            WHERE e IS NOT NULL OR u IS NOT NULL OR nested IS NOT NULL
                            FOREACH (_ IN CASE WHEN e IS NOT NULL THEN [1] ELSE [] END |
                                MERGE (e)-[:MEMBER_OF]->(g)
                            )
                            FOREACH (_ IN CASE WHEN u IS NOT NULL THEN [1] ELSE [] END |
                                MERGE (u)-[:MEMBER_OF]->(g)
                            )
                            FOREACH (_ IN CASE WHEN nested IS NOT NULL THEN [1] ELSE [] END |
                                MERGE (nested)-[:MEMBER_OF]->(g)
                            )
                            RETURN CASE 
                                WHEN e IS NOT NULL OR u IS NOT NULL OR nested IS NOT NULL THEN 1 
                                ELSE 0 
                            END as created
                        ", new
                        {
                            groupId = group.Id,
                            memberId
                        });

                        var record = await result.SingleAsync();
                        if (record["created"].As<int>() == 1)
                        {
                            totalRelationships++;
                        }
                    }
                });

                if (group.Members.Count > 0)
                {
                    _logger.LogDebug("Stored {MemberCount} members for group {GroupName}", 
                        group.Members.Count, group.DisplayName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing members for security group {DisplayName}", group.DisplayName);
            }
        }

        _logger.LogInformation("Security group members stored successfully. Total relationships created: {TotalRelationships}", 
            totalRelationships);
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
    }
}
