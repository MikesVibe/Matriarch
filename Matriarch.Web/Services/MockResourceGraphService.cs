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

    public Task<List<KeyVaultDto>> FetchKeyVaultAccessPoliciesForPrincipalsAsync(List<string> principalIds)
    {
        _logger.LogInformation("Mock: Fetching Key Vault access policies for {Count} principals", principalIds.Count);

        // Return mock Key Vault access policies
        var mockKeyVaults = new List<KeyVaultDto>
        {
            new KeyVaultDto
            {
                Id = "/subscriptions/sub-123/resourceGroups/rg-keyvault/providers/Microsoft.KeyVault/vaults/kv-production-001",
                Name = "kv-production-001",
                TenantId = "tenant-123",
                AccessPolicies = new List<AccessPolicyEntryDto>
                {
                    new AccessPolicyEntryDto
                    {
                        TenantId = "tenant-123",
                        ObjectId = principalIds.FirstOrDefault() ?? "mock-principal-id",
                        ApplicationId = string.Empty,
                        KeyPermissions = new List<string> { "Get", "List", "Create" },
                        SecretPermissions = new List<string> { "Get", "List", "Set" },
                        CertificatePermissions = new List<string> { "Get", "List" },
                        StoragePermissions = new List<string>()
                    }
                }
            }
        };

        return Task.FromResult(mockKeyVaults);
    }
}
