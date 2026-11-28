using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("customer_address_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(a => a.AddressType)
            .HasColumnName("address_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CustomerAddressType.Delivery)
            .IsRequired();

        builder.Property(a => a.Label)
            .HasColumnName("label")
            .HasMaxLength(100);

        builder.Property(a => a.RecipientName)
            .HasColumnName("recipient_name")
            .HasMaxLength(200);

        builder.Property(a => a.AddressLine1)
            .HasColumnName("address_line_1")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(a => a.AddressLine2)
            .HasColumnName("address_line_2")
            .HasMaxLength(255);

        builder.Property(a => a.City)
            .HasColumnName("city")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.StateOrProvince)
            .HasColumnName("state_or_province")
            .HasMaxLength(100);

        builder.Property(a => a.PostalCode)
            .HasColumnName("postal_code")
            .HasMaxLength(30);

        builder.Property(a => a.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(50);

        builder.Property(a => a.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization);

        builder.Property(a => a.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50);

        builder.Property(a => a.DeliveryInstructions)
            .HasColumnName("delivery_instructions")
            .HasMaxLength(500);

        builder.Property(a => a.IsDefault)
            .HasColumnName("is_default")
            .HasDefaultValue(false);

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(a => a.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(a => a.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(a => a.DeletedAt).HasColumnName("deleted_at");
        builder.Property(a => a.DeletedBy).HasColumnName("deleted_by");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(a => new { a.ClientId, a.CustomerId })
            .HasDatabaseName("ix_customer_addresses_client_customer");

        builder.HasOne(a => a.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
