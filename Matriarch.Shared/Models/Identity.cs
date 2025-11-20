namespace Matriarch.Shared.Models;

public class Identity
{
    public string ObjectId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RoleAssignment
{
    public string Id { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
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
}
