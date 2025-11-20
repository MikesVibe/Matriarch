using Matriarch.Models;
using Matriarch.Shared.Models;

namespace Matriarch.Services;

/// <summary>
/// Service for managing SQLite database operations for Matriarch data.
/// </summary>
public interface ISqliteService
{
    /// <summary>
    /// Initializes the SQLite database, creating tables if they don't exist.
    /// </summary>
    Task InitializeDatabaseAsync();
    
    /// <summary>
    /// Stores Azure data (app registrations, enterprise apps, security groups, and role assignments) in the SQLite database.
    /// Maps DTOs to database entities and handles group memberships.
    /// </summary>
    Task StoreDataAsync(
        List<AppRegistrationDto> appRegistrations,
        List<EnterpriseApplicationDto> enterpriseApps,
        List<SecurityGroupDto> securityGroups,
        List<RoleAssignmentDto> roleAssignments);
    
    /// <summary>
    /// Retrieves identity role assignments from the database, including security group memberships.
    /// Handles circular group references by tracking visited groups during recursive traversal.
    /// </summary>
    /// <param name="objectId">The object ID of the identity to retrieve.</param>
    /// <returns>Identity role assignment result, or null if identity not found.</returns>
    Task<IdentityRoleAssignmentResult?> GetIdentityRoleAssignmentsAsync(string objectId);
}
