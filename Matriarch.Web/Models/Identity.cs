namespace Matriarch.Web.Models;

public enum IdentityType
{
    User,
    Group,
    ServicePrincipal,
    UserAssignedManagedIdentity,
    SystemAssignedManagedIdentity
}

public class Identity
{
    public string ObjectId { get; set; } = string.Empty; // For Service Principals, this is the Enterprise Application ObjectId
    public string ApplicationId { get; set; } = string.Empty; // ClientId / ApplicationId (AppId)
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IdentityType Type { get; set; } = IdentityType.User;
    public string? ServicePrincipalType { get; set; } // For SP: "Application", "ManagedIdentity"
    public string? AppRegistrationId { get; set; } // ObjectId of the linked App Registration (for SP)
}

public class RoleAssignment
{
    public string Id { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
}

public class ApiPermission
{
    public string Id { get; set; } = string.Empty;
    public string ResourceDisplayName { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string PermissionType { get; set; } = string.Empty;
    public string PermissionValue { get; set; } = string.Empty;
}

public class SecurityGroup
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<RoleAssignment> RoleAssignments { get; set; } = new();
    public List<SecurityGroup> ParentGroups { get; set; } = new();
}

public class IdentityRoleAssignmentResult
{
    public Identity Identity { get; set; } = new();
    public List<RoleAssignment> DirectRoleAssignments { get; set; } = new();
    public List<SecurityGroup> SecurityGroups { get; set; } = new();
    public List<ApiPermission> ApiPermissions { get; set; } = new();
}

public class IdentitySearchResult
{
    public List<Identity> Identities { get; set; } = new();
    public bool HasMultipleResults => Identities.Count > 1;
}
