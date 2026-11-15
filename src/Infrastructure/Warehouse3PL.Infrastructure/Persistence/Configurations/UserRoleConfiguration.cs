using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(ur => ur.Id);
        builder.Property(ur => ur.Id)
            .HasColumnName("user_role_id")
            .ValueGeneratedOnAdd();

        builder.Property(ur => ur.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ur => ur.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(ur => ur.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        builder.Property(ur => ur.AssignedBy)
            .HasColumnName("assigned_by");

        builder.HasIndex(ur => new { ur.UserId, ur.RoleId })
            .IsUnique();
    }
}

