using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientOperationalPreferenceConfiguration : IEntityTypeConfiguration<ClientOperationalPreference>
{
    public void Configure(EntityTypeBuilder<ClientOperationalPreference> builder)
    {
        builder.ToTable("client_operational_preferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("preference_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(p => p.DefaultReceivingZoneId)
            .HasColumnName("default_receiving_zone_id");

        builder.Property(p => p.DefaultShippingZoneId)
            .HasColumnName("default_shipping_zone_id");

        builder.Property(p => p.RequireAsnBeforeReceiving)
            .HasColumnName("require_asn_before_receiving")
            .HasDefaultValue(false);

        builder.Property(p => p.AllowPartialShipments)
            .HasColumnName("allow_partial_shipments")
            .HasDefaultValue(true);

        builder.Property(p => p.RequireQcInspectionOnReturns)
            .HasColumnName("require_qc_inspection_on_returns")
            .HasDefaultValue(false);

        builder.Property(p => p.AllowLotMixingInLocation)
            .HasColumnName("allow_lot_mixing_in_location")
            .HasDefaultValue(false);

        builder.Property(p => p.DefaultOrderPriority)
            .HasColumnName("default_order_priority")
            .HasDefaultValue(1);

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(p => p.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => p.ClientId)
            .IsUnique()
            .HasDatabaseName("ux_client_op_prefs_client_default")
            .HasFilter("facility_id IS NULL");

        builder.HasIndex(p => new { p.ClientId, p.FacilityId })
            .IsUnique()
            .HasDatabaseName("ux_client_op_prefs_client_facility")
            .HasFilter("facility_id IS NOT NULL");

        builder.HasOne(p => p.Client)
            .WithMany(c => c.OperationalPreferences)
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DefaultReceivingZone)
            .WithMany()
            .HasForeignKey(p => p.DefaultReceivingZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DefaultShippingZone)
            .WithMany()
            .HasForeignKey(p => p.DefaultShippingZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
