namespace Matriarch.Models;

public class RoleAssignment
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string PrincipalType { get; set; } = string.Empty;
    public string RoleDefinitionId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}

public class EnterpriseApplication
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> GroupMemberships { get; set; } = new();
    public List<RoleAssignment> RoleAssignments { get; set; } = new();
}

public class AppRegistration
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<FederatedCredential> FederatedCredentials { get; set; } = new();
    public string? ServicePrincipalId { get; set; }
}

public class FederatedCredential
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<string> Audiences { get; set; } = new();
}

public class SecurityGroup
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<RoleAssignment> RoleAssignments { get; set; } = new();
}
