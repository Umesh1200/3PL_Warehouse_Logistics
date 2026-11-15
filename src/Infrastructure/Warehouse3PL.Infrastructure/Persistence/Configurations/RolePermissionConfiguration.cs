using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rp => rp.Id);
        builder.Property(rp => rp.Id)
            .HasColumnName("role_perm_id")
            .ValueGeneratedOnAdd();

        builder.Property(rp => rp.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(rp => rp.PermissionKey)
            .HasColumnName("permission_key")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(rp => rp.AccessLevel)
            .HasColumnName("access_level")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionKey })
            .IsUnique();

        builder.Property(rp => rp.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(rp => rp.CreatedBy).HasColumnName("created_by");
        builder.Property(rp => rp.UpdatedAt).HasColumnName("updated_at");
        builder.Property(rp => rp.UpdatedBy).HasColumnName("updated_by");
    }
}

