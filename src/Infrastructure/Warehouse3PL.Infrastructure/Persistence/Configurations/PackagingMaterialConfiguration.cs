using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PackagingMaterialConfiguration : IEntityTypeConfiguration<PackagingMaterial>
{
    public void Configure(EntityTypeBuilder<PackagingMaterial> builder)
    {
        builder.ToTable("packaging_materials");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("material_id")
            .ValueGeneratedNever();

        builder.Property(m => m.ClientId)
            .HasColumnName("client_id");

        builder.Property(m => m.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(m => m.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(m => m.MaterialType)
            .HasColumnName("material_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(m => m.LengthCm)
            .HasColumnName("length_cm")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(m => m.WidthCm)
            .HasColumnName("width_cm")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(m => m.HeightCm)
            .HasColumnName("height_cm")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(m => m.MaxWeightKg)
            .HasColumnName("max_weight_kg")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(m => m.TareWeightKg)
            .HasColumnName("tare_weight_kg")
            .HasPrecision(14, 4)
            .IsRequired();

        builder.Property(m => m.CostPerUnit)
            .HasColumnName("cost_per_unit")
            .HasPrecision(14, 4);

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Ignore(m => m.CalculatedVolumeCm3);

        // Soft delete
        builder.Property(m => m.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(m => m.DeletedAt).HasColumnName("deleted_at");
        builder.Property(m => m.DeletedBy).HasColumnName("deleted_by");

        // Auditing
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.CreatedBy).HasColumnName("created_by");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(m => new { m.FacilityId, m.Code });
        builder.HasIndex(m => m.ClientId);
        builder.HasIndex(m => m.IsActive);

        // Relationships
        builder.HasOne(m => m.Client)
            .WithMany()
            .HasForeignKey(m => m.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Facility)
            .WithMany()
            .HasForeignKey(m => m.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

