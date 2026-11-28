using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SupplierContactConfiguration : IEntityTypeConfiguration<SupplierContact>
{
    public void Configure(EntityTypeBuilder<SupplierContact> builder)
    {
        builder.ToTable("supplier_contacts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("supplier_contact_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(c => c.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.RoleTitle)
            .HasColumnName("role_title")
            .HasMaxLength(100);

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50);

        builder.Property(c => c.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false);

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(c => c.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
        builder.Property(c => c.DeletedBy).HasColumnName("deleted_by");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(c => new { c.ClientId, c.SupplierId })
            .HasDatabaseName("ix_supplier_contacts_client_supplier");

        builder.HasOne(c => c.Supplier)
            .WithMany(s => s.Contacts)
            .HasForeignKey(c => c.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
