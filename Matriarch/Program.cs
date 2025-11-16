using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Matriarch.Configuration;
using Matriarch.Services;

namespace Matriarch;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Bind settings
        var settings = new AppSettings();
        configuration.Bind(settings);

        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConfiguration(configuration.GetSection("Logging"))
                .AddConsole();
        });

        var logger = loggerFactory.CreateLogger<Program>();

        try
        {
            logger.LogInformation("Starting Matriarch - Azure to Neo4j Data Integration");

            // Validate configuration
            ValidateConfiguration(settings, logger);

            // Initialize services
            var azureDataService = new AzureDataService(settings, loggerFactory.CreateLogger<AzureDataService>());
            var neo4jService = new Neo4jService(settings, loggerFactory.CreateLogger<Neo4jService>());


            var roleAssignmentsTask = await azureDataService.FetchRoleAssignmentsAsync();

            if (roleAssignmentsTask.Count > 0)
            {
                var test = roleAssignmentsTask.First();
                logger.LogInformation("Found {count} of Role Assignment/s", roleAssignmentsTask.Count);
                //            logger.LogInformation("Role Assignment: Id={Id}, RoleDefinitionId={RoleDefinitionId}, PrincipalId={PrincipalId}, Scope={Scope}",
                //test.Id, test.RoleDefinitionId, test.PrincipalId, test.Scope);


                roleAssignmentsTask.ForEach(ra =>
                    //logger.LogInformation("Role Assignment: Id={Id}, RoleDefinitionId={RoleDefinitionId}, PrincipalId={PrincipalId}, Scope={Scope}, RoleName={RoleName}",
                    //ra.Id, ra.RoleDefinitionId, ra.PrincipalId, ra.Scope, ra.RoleName)

                    logger.LogInformation("PrincipalId={PrincipalId}, RoleName={RoleName}",
                    ra.PrincipalId, ra.RoleName)
                    );

            }
            else
            {
                logger.LogWarning("No role assignments found");
            }
            //// Initialize Neo4j database schema
            //await neo4jService.InitializeDatabaseAsync();

            //// Fetch data from Azure
            //logger.LogInformation("=== Fetching data from Azure ===");

            //var appRegistrationsTask = azureDataService.FetchAppRegistrationsAsync();
            //var enterpriseAppsTask = azureDataService.FetchEnterpriseApplicationsAsync();
            //var securityGroupsTask = azureDataService.FetchSecurityGroupsAsync();
            //var roleAssignmentsTask = azureDataService.FetchRoleAssignmentsAsync();

            //await Task.WhenAll(appRegistrationsTask, enterpriseAppsTask, securityGroupsTask, roleAssignmentsTask);

            //var appRegistrations = await appRegistrationsTask;
            //var enterpriseApps = await enterpriseAppsTask;
            //var securityGroups = await securityGroupsTask;
            //var roleAssignments = await roleAssignmentsTask;

            //// Link service principal IDs to app registrations
            //LinkAppRegistrationsToEnterpriseApps(appRegistrations, enterpriseApps, logger);

            //// Store data in Neo4j
            //logger.LogInformation("=== Storing data in Neo4j ===");

            //await neo4jService.StoreAppRegistrationsAsync(appRegistrations);
            //await neo4jService.StoreSecurityGroupsAsync(securityGroups);
            //await neo4jService.StoreEnterpriseApplicationsAsync(enterpriseApps);
            //await neo4jService.StoreRoleAssignmentsAsync(roleAssignments, enterpriseApps, securityGroups);
            //await neo4jService.StoreGroupMembershipsAsync(enterpriseApps);

            //logger.LogInformation("=== Data integration completed successfully ===");
            //logger.LogInformation($"Summary:");
            //logger.LogInformation($"  - App Registrations: {appRegistrations.Count}");
            //logger.LogInformation($"  - Enterprise Applications: {enterpriseApps.Count}");
            //logger.LogInformation($"  - Security Groups: {securityGroups.Count}");
            //logger.LogInformation($"  - Role Assignments: {roleAssignments.Count}");

            //await neo4jService.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during execution");
            Environment.Exit(1);
        }
    }

    private static void ValidateConfiguration(AppSettings settings, ILogger logger)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.Azure.TenantId))
            errors.Add("Azure TenantId is not configured");
        if (string.IsNullOrWhiteSpace(settings.Azure.SubscriptionId))
            errors.Add("Azure SubscriptionId is not configured");
        if (string.IsNullOrWhiteSpace(settings.Azure.ClientId))
            errors.Add("Azure ClientId is not configured");
        if (string.IsNullOrWhiteSpace(settings.Azure.ClientSecret))
            errors.Add("Azure ClientSecret is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Uri))
            errors.Add("Neo4j Uri is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Username))
            errors.Add("Neo4j Username is not configured");
        if (string.IsNullOrWhiteSpace(settings.Neo4j.Password))
            errors.Add("Neo4j Password is not configured");

        if (errors.Count > 0)
        {
            logger.LogError("Configuration validation failed:");
            foreach (var error in errors)
            {
                logger.LogError($"  - {error}");
            }
            logger.LogInformation("Please update appsettings.json or set environment variables.");
            throw new InvalidOperationException("Configuration validation failed");
        }
    }

    private static void LinkAppRegistrationsToEnterpriseApps(
        List<Models.AppRegistration> appRegistrations,
        List<Models.EnterpriseApplication> enterpriseApps,
        ILogger logger)
    {
        logger.LogInformation("Linking app registrations to enterprise applications...");

        var enterpriseAppsByAppId = enterpriseApps.ToDictionary(e => e.AppId, e => e);

        foreach (var appReg in appRegistrations)
        {
            if (enterpriseAppsByAppId.TryGetValue(appReg.AppId, out var enterpriseApp))
            {
                appReg.ServicePrincipalId = enterpriseApp.Id;
            }
        }

        logger.LogInformation($"Linked {appRegistrations.Count(a => a.ServicePrincipalId != null)} app registrations to enterprise applications");
    }
}
