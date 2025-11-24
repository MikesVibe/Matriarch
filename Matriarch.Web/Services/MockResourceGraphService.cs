using Matriarch.Web.Models;
using Microsoft.Extensions.Logging;

namespace Matriarch.Web.Services;

/// <summary>
/// Mock implementation of IResourceGraphService for testing purposes.
/// </summary>
public class MockResourceGraphService : IResourceGraphService
{
    private readonly ILogger<MockResourceGraphService> _logger;

    public MockResourceGraphService(ILogger<MockResourceGraphService> logger)
    {
        _logger = logger;
    }

    public Task<List<AzureRoleAssignmentDto>> FetchRoleAssignmentsForPrincipalsAsync(List<string> principalIds)
    {
        _logger.LogInformation("Mock: Fetching role assignments for {Count} principals", principalIds.Count);

        // Return mock role assignments
        var mockRoleAssignments = new List<AzureRoleAssignmentDto>
        {
            new AzureRoleAssignmentDto
            {
                Id = "/subscriptions/sub-123/providers/Microsoft.Authorization/roleAssignments/ra-1",
                PrincipalId = principalIds.FirstOrDefault() ?? "mock-principal-id",
                PrincipalType = "ServicePrincipal",
                RoleDefinitionId = "/subscriptions/sub-123/providers/Microsoft.Authorization/roleDefinitions/role-def-1",
                RoleName = "Contributor",
                Scope = "/subscriptions/sub-123/resourceGroups/rg-dev"
            },
            new AzureRoleAssignmentDto
            {
                Id = "/subscriptions/sub-123/providers/Microsoft.Authorization/roleAssignments/ra-2",
                PrincipalId = principalIds.FirstOrDefault() ?? "mock-principal-id",
                PrincipalType = "ServicePrincipal",
                RoleDefinitionId = "/subscriptions/sub-123/providers/Microsoft.Authorization/roleDefinitions/role-def-2",
                RoleName = "Reader",
                Scope = "/subscriptions/sub-123"
            }
        };

        return Task.FromResult(mockRoleAssignments);
    }
}
