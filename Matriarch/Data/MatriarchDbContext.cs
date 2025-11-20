using Microsoft.EntityFrameworkCore;

namespace Matriarch.Data;

public class MatriarchDbContext : DbContext
{
    public MatriarchDbContext(DbContextOptions<MatriarchDbContext> options) : base(options)
    {
    }

    public DbSet<AppRegistrationEntity> AppRegistrations => Set<AppRegistrationEntity>();
    public DbSet<EnterpriseApplicationEntity> EnterpriseApplications => Set<EnterpriseApplicationEntity>();
    public DbSet<SecurityGroupEntity> SecurityGroups => Set<SecurityGroupEntity>();
    public DbSet<RoleAssignmentEntity> RoleAssignments => Set<RoleAssignmentEntity>();
    public DbSet<FederatedCredentialEntity> FederatedCredentials => Set<FederatedCredentialEntity>();
    public DbSet<GroupMembershipEntity> GroupMemberships => Set<GroupMembershipEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure AppRegistration entity
        modelBuilder.Entity<AppRegistrationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppId);
        });

        // Configure EnterpriseApplication entity
        modelBuilder.Entity<EnterpriseApplicationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppId);
        });

        // Configure SecurityGroup entity
        modelBuilder.Entity<SecurityGroupEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Configure RoleAssignment entity
        modelBuilder.Entity<RoleAssignmentEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PrincipalId);
        });

        // Configure FederatedCredential entity
        modelBuilder.Entity<FederatedCredentialEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppRegistrationId);
        });

        // Configure GroupMembership entity - represents member_of relationships
        modelBuilder.Entity<GroupMembershipEntity>(entity =>
        {
            entity.HasKey(e => new { e.MemberId, e.GroupId });
            entity.HasIndex(e => e.MemberId);
            entity.HasIndex(e => e.GroupId);
        });
    }
}
