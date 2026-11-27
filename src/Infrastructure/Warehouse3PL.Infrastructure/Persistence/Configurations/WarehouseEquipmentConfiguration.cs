using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class WarehouseEquipmentConfiguration : IEntityTypeConfiguration<WarehouseEquipment>
{
    public void Configure(EntityTypeBuilder<WarehouseEquipment> builder)
    {
        builder.ToTable("warehouse_equipment");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("equipment_id").ValueGeneratedNever();
        builder.Property(e => e.FacilityId).HasColumnName("facility_id").IsRequired();
        builder.Property(e => e.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.NormalizedEquipmentCode).HasColumnName("normalized_equipment_code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(e => e.EquipmentType).HasColumnName("equipment_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.SerialNumber).HasColumnName("serial_number").HasMaxLength(100);
        builder.Property(e => e.MaxLiftHeightCm).HasColumnName("max_lift_height_cm").HasPrecision(10, 2);
        builder.Property(e => e.MaxLoadKg).HasColumnName("max_load_kg").HasPrecision(10, 2);
        builder.Property(e => e.RequiresCertification).HasColumnName("requires_certification").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Remarks).HasColumnName("remarks").HasMaxLength(500);
        builder.Property(e => e.ConfigRevision).HasColumnName("config_revision").HasDefaultValue(1L).IsConcurrencyToken();

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(e => e.FacilityId);
        builder.HasIndex(e => new { e.FacilityId, e.NormalizedEquipmentCode })
            .HasDatabaseName("uq_warehouse_equipment_fac_norm_code")
            .IsUnique();

        builder.HasOne(e => e.Facility)
            .WithMany()
            .HasForeignKey(e => e.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
