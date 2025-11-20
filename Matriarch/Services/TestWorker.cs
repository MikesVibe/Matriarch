using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Matriarch.Services;

namespace Matriarch.Services;

public class TestWorker : IHostedService
{
    private readonly ILogger<TestWorker> _logger;
    private readonly IAzureDataService _azureDataService;
    private readonly IHostApplicationLifetime _appLifetime;
    private const string TestGroupId = "9dfa1964-c283-4c15-84f9-385e07cb306f";

    public TestWorker(
        ILogger<TestWorker> logger,
        IAzureDataService azureDataService,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _azureDataService = azureDataService;
        _appLifetime = appLifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("=== Starting Test Worker ===");
            _logger.LogInformation("Testing member fetch for group ID: {GroupId}", TestGroupId);

            // Fetch all security groups first to get the group details
            var securityGroups = await _azureDataService.FetchSecurityGroupsAsync();
            
            // Find the specific group
            var targetGroup = securityGroups.FirstOrDefault(g => g.Id == TestGroupId);
            
            if (targetGroup == null)
            {
                _logger.LogWarning("Group with ID {GroupId} not found!", TestGroupId);
                _logger.LogInformation("Available groups: {Count}", securityGroups.Count);
            }
            else
            {
                _logger.LogInformation("Found group: {DisplayName} (ID: {Id})", 
                    targetGroup.DisplayName, targetGroup.Id);
                
                // Fetch members for just this one group
                await _azureDataService.FetchMembersForSecurityGroupsAsync(new List<Models.SecurityGroupDto> { targetGroup });
                
                _logger.LogInformation("=== Member Fetch Results ===");
                _logger.LogInformation("Group: {DisplayName}", targetGroup.DisplayName);
                _logger.LogInformation("Total Members: {Count}", targetGroup.Members.Count);
                
                if (targetGroup.Members.Count > 0)
                {
                    _logger.LogInformation("Members breakdown:");
                    var userCount = targetGroup.Members.Count(m => m.Type == Models.MemberType.User);
                    var groupCount = targetGroup.Members.Count(m => m.Type == Models.MemberType.Group);
                    var spCount = targetGroup.Members.Count(m => m.Type == Models.MemberType.ServicePrincipal);
                    var deviceCount = targetGroup.Members.Count(m => m.Type == Models.MemberType.Device);
                    var unknownCount = targetGroup.Members.Count(m => m.Type == Models.MemberType.Unknown);
                    
                    _logger.LogInformation("  - Users: {Count}", userCount);
                    _logger.LogInformation("  - Groups: {Count}", groupCount);
                    _logger.LogInformation("  - Service Principals: {Count}", spCount);
                    _logger.LogInformation("  - Devices: {Count}", deviceCount);
                    _logger.LogInformation("  - Unknown: {Count}", unknownCount);
                    
                    _logger.LogInformation("Sample members (first 10):");
                    foreach (var member in targetGroup.Members.Take(10))
                    {
                        _logger.LogInformation("  - {DisplayName} ({Type}) [ID: {Id}]", 
                            member.DisplayName, 
                            member.Type, 
                            member.Id);
                        
                        if (!string.IsNullOrEmpty(member.UserPrincipalName))
                        {
                            _logger.LogInformation("    UPN: {UPN}", member.UserPrincipalName);
                        }
                        if (!string.IsNullOrEmpty(member.Mail))
                        {
                            _logger.LogInformation("    Mail: {Mail}", member.Mail);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Group has no members.");
                }
            }

            _logger.LogInformation("=== Test Worker Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during test execution");
            throw;
        }
        finally
        {
            // Signal the application to stop
            _appLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Test worker is stopping");
        return Task.CompletedTask;
    }
}
