using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Matriarch.Models;

namespace Matriarch.Services;

public class MatriarchWorker : IHostedService
{
    private readonly ILogger<MatriarchWorker> _logger;
    private readonly AzureDataService _azureDataService;
    private readonly Neo4jService _neo4jService;
    private readonly IHostApplicationLifetime _appLifetime;

    public MatriarchWorker(
        ILogger<MatriarchWorker> logger,
        AzureDataService azureDataService,
        Neo4jService neo4jService,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _azureDataService = azureDataService;
        _neo4jService = neo4jService;
        _appLifetime = appLifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Matriarch - Azure to Neo4j Data Integration");

            // Initialize Neo4j database schema
            //await _neo4jService.InitializeDatabaseAsync();

            // Fetch data from Azure
            _logger.LogInformation("=== Fetching data from Azure ===");

            var roleAssignments = await _azureDataService.FetchRoleAssignmentsAsync();
            _logger.LogInformation("Found {count} of Role Assignment/s", roleAssignments.Count);

            var appRegistrations = await _azureDataService.FetchAppRegistrationsAsync();
            _logger.LogInformation("Found {count} of App Registration/s", appRegistrations.Count);

            _logger.LogInformation("Starting to fetch enterprise applications...");
            var enterpriseApps = await _azureDataService.FetchEnterpriseApplicationsAsync();
            _logger.LogInformation("Successfully fetched {Count} enterprise applications", enterpriseApps.Count);

            var securityGroups = await _azureDataService.FetchSecurityGroupsAsync();
            _logger.LogInformation("Found {count} of Security Group/s", securityGroups.Count);

            // Fetch members for security groups
            await _azureDataService.FetchMembersForSecurityGroupsAsync(securityGroups);
            _logger.LogInformation("Fetched members for {count} security groups", securityGroups.Count);

            // Link service principal IDs to app registrations
            var linkedAppsDict = LinkAppRegistrationsToEnterpriseApps(appRegistrations, enterpriseApps);

            // Fetch group memberships only for enterprise apps linked to app registrations
            if (linkedAppsDict.Count > 0)
            {
                var linkedEnterpriseApps = linkedAppsDict.Values.Select(pair => pair.EnterpriseApp).ToList();
                _logger.LogInformation("Fetching group memberships for {Count} linked enterprise applications...", linkedEnterpriseApps.Count);
                await _azureDataService.FetchGroupMembershipsForLinkedAppsAsync(linkedEnterpriseApps);
            }
            else
            {
                _logger.LogWarning("No enterprise applications are linked to app registrations");
            }

            // Store data in Neo4j
            _logger.LogInformation("=== Storing data in Neo4j ===");

            //await _neo4jService.StoreAppRegistrationsAsync(appRegistrations);
            //await _neo4jService.StoreSecurityGroupsAsync(securityGroups);
            //await _neo4jService.StoreEnterpriseApplicationsAsync(enterpriseApps);
            //await _neo4jService.StoreRoleAssignmentsAsync(roleAssignments, enterpriseApps, securityGroups);
            //await _neo4jService.StoreGroupMembershipsAsync(enterpriseApps);
            //await _neo4jService.StoreSecurityGroupMembersAsync(securityGroups);

            _logger.LogInformation("=== Data integration completed successfully ===");
            _logger.LogInformation("Summary:");
            _logger.LogInformation("  - App Registrations: {Count}", appRegistrations.Count);
            _logger.LogInformation("  - Enterprise Applications: {Count}", enterpriseApps.Count);
            _logger.LogInformation("  - Security Groups: {Count}", securityGroups.Count);
            _logger.LogInformation("  - Role Assignments: {Count}", roleAssignments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during execution");
            _appLifetime.StopApplication();
            throw;
        }

        // Signal the application to stop
        _appLifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Matriarch worker is stopping");
        return Task.CompletedTask;
    }

    private Dictionary<string, (AppRegistrationDto AppRegistration, EnterpriseApplicationDto EnterpriseApp)> LinkAppRegistrationsToEnterpriseApps(
        List<AppRegistrationDto> appRegistrations,
        List<EnterpriseApplicationDto> enterpriseApps)
    {
        _logger.LogInformation("Linking app registrations to enterprise applications...");

        var enterpriseAppsByAppId = enterpriseApps.ToDictionary(e => e.AppId, e => e);
        var linkedApps = new Dictionary<string, (AppRegistrationDto AppRegistration, EnterpriseApplicationDto EnterpriseApp)>();

        foreach (var appReg in appRegistrations)
        {
            if (enterpriseAppsByAppId.TryGetValue(appReg.AppId, out var enterpriseApp))
            {
                appReg.ServicePrincipalId = enterpriseApp.Id;
                linkedApps[appReg.AppId] = (appReg, enterpriseApp);
            }
        }

        _logger.LogInformation("Linked {Count} app registrations to enterprise applications", linkedApps.Count);

        return linkedApps;
    }
}
