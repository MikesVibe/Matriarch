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
