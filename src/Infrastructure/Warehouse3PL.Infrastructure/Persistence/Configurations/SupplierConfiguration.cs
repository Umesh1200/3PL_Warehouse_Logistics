using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Inbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("supplier_id")
            .ValueGeneratedNever();

        builder.Property(s => s.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        // Enforce tenant-aware supplier code uniqueness
        builder.HasIndex(s => new { s.ClientId, s.SupplierCode })
            .IsUnique();

        builder.Property(s => s.SupplierCode)
            .HasColumnName("supplier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.SupplierName)
            .HasColumnName("supplier_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(s => s.ContactName)
            .HasColumnName("contact_name")
            .HasMaxLength(150);

        builder.Property(s => s.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        builder.Property(s => s.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50);

        builder.Property(s => s.AddressLine1)
            .HasColumnName("address_line1")
            .HasMaxLength(255);

        builder.Property(s => s.AddressLine2)
            .HasColumnName("address_line2")
            .HasMaxLength(255);

        builder.Property(s => s.City)
            .HasColumnName("city")
            .HasMaxLength(100);

        builder.Property(s => s.State)
            .HasColumnName("state")
            .HasMaxLength(100);

        builder.Property(s => s.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(20);

        builder.Property(s => s.Country)
            .HasColumnName("country")
            .HasMaxLength(100);

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(s => s.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(s => s.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(s => s.DeletedBy)
            .HasColumnName("deleted_by");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(s => s.UpdatedBy)
            .HasColumnName("updated_by");

        builder.HasOne(s => s.Client)
            .WithMany()
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
