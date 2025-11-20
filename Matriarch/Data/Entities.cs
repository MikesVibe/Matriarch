using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matriarch.Data;

// AppRegistration entity
public class AppRegistrationEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    public string AppId { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
    
    public string? ServicePrincipalId { get; set; }
}

// EnterpriseApplication (Service Principal) entity
public class EnterpriseApplicationEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    public string AppId { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
}

// SecurityGroup entity
public class SecurityGroupEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
    
    public string? Description { get; set; }
}

// RoleAssignment entity
public class RoleAssignmentEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    public string PrincipalId { get; set; } = string.Empty;
    
    public string PrincipalType { get; set; } = string.Empty;
    
    public string RoleDefinitionId { get; set; } = string.Empty;
    
    public string RoleName { get; set; } = string.Empty;
    
    public string Scope { get; set; } = string.Empty;
}

// FederatedCredential entity
public class FederatedCredentialEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    
    public string AppRegistrationId { get; set; } = string.Empty;
    
    public string Name { get; set; } = string.Empty;
    
    public string Issuer { get; set; } = string.Empty;
    
    public string Subject { get; set; } = string.Empty;
    
    public string Audiences { get; set; } = string.Empty; // Stored as JSON string
}

// GroupMembership entity - represents member_of relationships
// This handles both EnterpriseApp -> Group and Group -> Group relationships
public class GroupMembershipEntity
{
    public string MemberId { get; set; } = string.Empty; // Can be EnterpriseApp or SecurityGroup ID
    
    public string GroupId { get; set; } = string.Empty; // SecurityGroup ID
    
    public string MemberType { get; set; } = string.Empty; // "EnterpriseApplication" or "SecurityGroup"
}
