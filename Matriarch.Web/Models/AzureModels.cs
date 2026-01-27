namespace Matriarch.Web.Models;

public class RoleAssignmentDto
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string PrincipalType { get; set; } = string.Empty;
    public string RoleDefinitionId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}

public class EnterpriseApplicationDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> GroupMemberships { get; set; } = new();
    public List<RoleAssignmentDto> RoleAssignments { get; set; } = new();
}

public class AppRegistrationDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<FederatedCredentialDto> FederatedCredentials { get; set; } = new();
    public string? ServicePrincipalId { get; set; }
}

public class FederatedCredentialDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<string> Audiences { get; set; } = new();
}

public class GroupMemberDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MemberType Type { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Mail { get; set; }
}

public enum MemberType
{
    User,
    Group,
    ServicePrincipal,
    Device,
    Unknown
}

public class SecurityGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<GroupMemberDto> Members { get; set; } = new();
}

public class KeyVaultDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public List<AccessPolicyEntryDto> AccessPolicies { get; set; } = new();
}

public class AccessPolicyEntryDto
{
    public string TenantId { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public List<string> KeyPermissions { get; set; } = new();
    public List<string> SecretPermissions { get; set; } = new();
    public List<string> CertificatePermissions { get; set; } = new();
    public List<string> StoragePermissions { get; set; } = new();
}

public class ManagedIdentityResourceDto
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string IdentityType { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ManagedIdentityResourceId { get; set; } = string.Empty;
}

// Alias to unify with duplicate definition in GroupManagementService.cs
// Both definitions are identical and refer to role assignment data from Azure Resource Graph
public class AzureRoleAssignmentDto : RoleAssignmentDto { }

public class SubscriptionDto
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public List<string> ManagementGroupHierarchy { get; set; } = new(); // Ordered from root to child
}

public class ManagementGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ParentId { get; set; }
}

