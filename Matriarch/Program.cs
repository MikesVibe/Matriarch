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
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.TimestampFormat = "HH:mm:ss ";
                });
        });

        var logger = loggerFactory.CreateLogger<Program>();

        try
        {
            logger.LogInformation("Starting Matriarch - Azure to Neo4j Data Integration");

            // Validate configuration
            ValidateConfiguration(settings, logger);

            // Initialize services
            var cachingService = new CachingService(settings, loggerFactory.CreateLogger<CachingService>());
            var azureDataService = new AzureDataService(settings, loggerFactory.CreateLogger<AzureDataService>(), cachingService);
            var neo4jService = new Neo4jService(settings, loggerFactory.CreateLogger<Neo4jService>());

            //Initialize Neo4j database schema
            await neo4jService.InitializeDatabaseAsync();

            // Fetch data from Azure
            logger.LogInformation("=== Fetching data from Azure ===");

            var roleAssignments = await azureDataService.FetchRoleAssignmentsAsync();
            logger.LogInformation("Found {count} of Role Assignment/s", roleAssignments.Count);

            var appRegistrations = await azureDataService.FetchAppRegistrationsAsync();
            logger.LogInformation("Found {count} of App Registration/s", appRegistrations.Count);

            logger.LogInformation("Starting to fetch enterprise applications...");
            var enterpriseApps = await azureDataService.FetchEnterpriseApplicationsAsync();
            logger.LogInformation("Successfully fetched {Count} enterprise applications", enterpriseApps.Count);

            var securityGroups = await azureDataService.FetchSecurityGroupsAsync();
            logger.LogInformation("Found {count} of Security Group/s", securityGroups.Count);

            // Fetch members for security groups
            await azureDataService.FetchMembersForSecurityGroupsAsync(securityGroups);
            logger.LogInformation("Fetched members for {count} security groups", securityGroups.Count);

            // Link service principal IDs to app registrations
            var linkedAppsDict = LinkAppRegistrationsToEnterpriseApps(appRegistrations, enterpriseApps, logger);

            // Fetch group memberships only for enterprise apps linked to app registrations
            if (linkedAppsDict.Count > 0)
            {
                var linkedEnterpriseApps = linkedAppsDict.Values.Select(pair => pair.EnterpriseApp).ToList();
                logger.LogInformation("Fetching group memberships for {Count} linked enterprise applications...", linkedEnterpriseApps.Count);
                await azureDataService.FetchGroupMembershipsForLinkedAppsAsync(linkedEnterpriseApps);
            }
            else
            {
                logger.LogWarning("No enterprise applications are linked to app registrations");
            }

            // Store data in Neo4j
            logger.LogInformation("=== Storing data in Neo4j ===");

            //await neo4jService.StoreAppRegistrationsAsync(appRegistrations);
            //await neo4jService.StoreSecurityGroupsAsync(securityGroups);
            //await neo4jService.StoreEnterpriseApplicationsAsync(enterpriseApps);
            //await neo4jService.StoreRoleAssignmentsAsync(roleAssignments, enterpriseApps, securityGroups);
            //await neo4jService.StoreGroupMembershipsAsync(enterpriseApps);
            //await neo4jService.StoreSecurityGroupMembersAsync(securityGroups);

            logger.LogInformation("=== Data integration completed successfully ===");
            logger.LogInformation($"Summary:");
            logger.LogInformation($"  - App Registrations: {appRegistrations.Count}");
            logger.LogInformation($"  - Enterprise Applications: {enterpriseApps.Count}");
            logger.LogInformation($"  - Security Groups: {securityGroups.Count}");
            logger.LogInformation($"  - Role Assignments: {roleAssignments.Count}");

            await neo4jService.DisposeAsync();
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

    private static Dictionary<string, (Models.AppRegistrationDto AppRegistration, Models.EnterpriseApplicationDto EnterpriseApp)> LinkAppRegistrationsToEnterpriseApps(
        List<Models.AppRegistrationDto> appRegistrations,
        List<Models.EnterpriseApplicationDto> enterpriseApps,
        ILogger logger)
    {
        logger.LogInformation("Linking app registrations to enterprise applications...");

        var enterpriseAppsByAppId = enterpriseApps.ToDictionary(e => e.AppId, e => e);
        var linkedApps = new Dictionary<string, (Models.AppRegistrationDto AppRegistration, Models.EnterpriseApplicationDto EnterpriseApp)>();

        foreach (var appReg in appRegistrations)
        {
            if (enterpriseAppsByAppId.TryGetValue(appReg.AppId, out var enterpriseApp))
            {
                appReg.ServicePrincipalId = enterpriseApp.Id;
                linkedApps[appReg.AppId] = (appReg, enterpriseApp);
            }
        }

        logger.LogInformation($"Linked {linkedApps.Count} app registrations to enterprise applications");

        return linkedApps;
    }
}
