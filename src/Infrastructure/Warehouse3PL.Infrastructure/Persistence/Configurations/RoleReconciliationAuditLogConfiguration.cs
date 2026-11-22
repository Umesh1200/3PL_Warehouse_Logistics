using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RoleReconciliationAuditLogConfiguration : IEntityTypeConfiguration<RoleReconciliationAuditLog>
{
    public void Configure(EntityTypeBuilder<RoleReconciliationAuditLog> builder)
    {
        builder.ToTable("role_reconciliation_audit_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("audit_id")
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.UserType)
            .HasColumnName("user_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(x => x.RoleName)
            .HasColumnName("role_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.ReconciledAtUtc)
            .HasColumnName("reconciled_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ReconciledAtUtc);
    }
}
