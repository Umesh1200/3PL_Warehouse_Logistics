using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShippingAddressConfiguration : IEntityTypeConfiguration<ShippingAddress>
{
    public void Configure(EntityTypeBuilder<ShippingAddress> builder)
    {
        builder.ToTable("shipping_addresses");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("shipping_address_id").ValueGeneratedNever();

        builder.Property(s => s.OutboundOrderId).HasColumnName("order_id").IsRequired();
        builder.Property(s => s.RecipientName).HasColumnName("recipient_name").HasMaxLength(150).IsRequired();
        builder.Property(s => s.CompanyName).HasColumnName("company_name").HasMaxLength(150);
        builder.Property(s => s.AddressLine1).HasColumnName("address_line1").HasMaxLength(200).IsRequired();
        builder.Property(s => s.AddressLine2).HasColumnName("address_line2").HasMaxLength(200);
        builder.Property(s => s.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        builder.Property(s => s.StateOrProvince).HasColumnName("state_or_province").HasMaxLength(100).IsRequired();
        builder.Property(s => s.PostalCode).HasColumnName("postal_code").HasMaxLength(30).IsRequired();
        builder.Property(s => s.CountryCode).HasColumnName("country_code").HasMaxLength(10).IsRequired();
        builder.Property(s => s.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization)
            .IsRequired();
        builder.Property(s => s.PhoneNumber).HasColumnName("phone_number").HasMaxLength(50);
        builder.Property(s => s.DeliveryInstructions).HasColumnName("delivery_instructions").HasMaxLength(500);

        builder.Property(s => s.IsAmended).HasColumnName("is_amended").IsRequired();
        builder.Property(s => s.AmendedAtUtc).HasColumnName("amended_at_utc");
        builder.Property(s => s.AmendedByUserId).HasColumnName("amended_by_user_id");
        builder.Property(s => s.AmendmentReason).HasColumnName("amendment_reason").HasMaxLength(500);
        builder.Property(s => s.OriginalAddressSnapshot).HasColumnName("original_address_snapshot").HasMaxLength(1000);

        builder.Property(s => s.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        // Soft delete
        builder.Property(s => s.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");
        builder.Property(s => s.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(s => !s.IsDeleted);

        // 1:1 Unique Relationship
        builder.HasIndex(s => s.OutboundOrderId).IsUnique();
        builder.HasOne(s => s.OutboundOrder)
            .WithOne(o => o.ShippingAddress)
            .HasForeignKey<ShippingAddress>(s => s.OutboundOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

