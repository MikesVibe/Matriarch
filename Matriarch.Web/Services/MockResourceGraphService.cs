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

    public Task<List<ManagedIdentityResourceDto>> FetchManagedIdentitiesByTagAsync(string tagValue)
    {
        _logger.LogInformation("Mock: Fetching managed identities for tag: {TagValue}", tagValue);

        // Return mock managed identities
        var mockIdentities = new List<ManagedIdentityResourceDto>
        {
            new ManagedIdentityResourceDto
            {
                SubscriptionId = "sub-123",
                ResourceGroup = "rg-dev",
                ResourceId = "/subscriptions/sub-123/resourceGroups/rg-dev/providers/Microsoft.Web/sites/webapp-001",
                ResourceName = "webapp-001",
                ResourceType = "Microsoft.Web/sites",
                IdentityType = "SystemAssigned",
                PrincipalId = "principal-001",
                TenantId = "tenant-123",
                ManagedIdentityResourceId = string.Empty
            },
            new ManagedIdentityResourceDto
            {
                SubscriptionId = "sub-123",
                ResourceGroup = "rg-dev",
                ResourceId = "/subscriptions/sub-123/resourceGroups/rg-dev/providers/Microsoft.Compute/virtualMachines/vm-001",
                ResourceName = "vm-001",
                ResourceType = "Microsoft.Compute/virtualMachines",
                IdentityType = "UserAssigned",
                PrincipalId = "principal-002",
                TenantId = "tenant-123",
                ManagedIdentityResourceId = "/subscriptions/sub-123/resourceGroups/rg-identities/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-001"
            }
        };

        return Task.FromResult(mockIdentities);
    }

    public Task<List<SubscriptionDto>> FetchAllSubscriptionsAsync()
    {
        _logger.LogInformation("Mock: Fetching all subscriptions");

        var mockSubscriptions = new List<SubscriptionDto>
        {
            new SubscriptionDto
            {
                SubscriptionId = "sub-123",
                Name = "Development Subscription",
                TenantId = "tenant-123",
                ManagementGroupHierarchy = new List<string> { "Root Management Group", "Development" }
            },
            new SubscriptionDto
            {
                SubscriptionId = "sub-456",
                Name = "Production Subscription",
                TenantId = "tenant-123",
                ManagementGroupHierarchy = new List<string> { "Root Management Group", "Production" }
            }
        };

        return Task.FromResult(mockSubscriptions);
    }

    public Task<List<string>> FetchManagementGroupHierarchyAsync(string subscriptionId)
    {
        _logger.LogInformation("Mock: Fetching management group hierarchy for subscription: {SubscriptionId}", subscriptionId);

        var hierarchy = subscriptionId switch
        {
            "sub-123" => new List<string> { "Root Management Group", "Development" },
            "sub-456" => new List<string> { "Root Management Group", "Production" },
            _ => new List<string> { "Root Management Group" }
        };

        return Task.FromResult(hierarchy);
    }
}
