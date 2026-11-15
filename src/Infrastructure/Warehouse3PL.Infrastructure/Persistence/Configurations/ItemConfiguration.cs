using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("item_id")
            .ValueGeneratedNever();

        builder.Property(i => i.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        // Enforce tenant-aware SKU uniqueness
        builder.HasIndex(i => new { i.ClientId, i.Sku })
            .IsUnique();

        builder.Property(i => i.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(i => i.ShortName)
            .HasColumnName("short_name")
            .HasMaxLength(100);

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(14, 4);

        builder.Property(i => i.WeightKg)
            .HasColumnName("weight_kg")
            .HasPrecision(10, 3);

        builder.Property(i => i.LengthCm)
            .HasColumnName("length_cm")
            .HasPrecision(10, 2);

        builder.Property(i => i.WidthCm)
            .HasColumnName("width_cm")
            .HasPrecision(10, 2);

        builder.Property(i => i.HeightCm)
            .HasColumnName("height_cm")
            .HasPrecision(10, 2);

        builder.Property(i => i.VolumeCm3)
            .HasColumnName("volume_cm3")
            .HasPrecision(14, 2);

        builder.Property(i => i.AbcVelocity)
            .HasColumnName("abc_velocity")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(i => i.StorageTempClass)
            .HasColumnName("storage_temp_class")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.HazardClass)
            .HasColumnName("hazard_class")
            .HasMaxLength(50);

        builder.Property(i => i.StackabilityFactor)
            .HasColumnName("stackability_factor")
            .HasDefaultValue(3);

        builder.Property(i => i.IsLotTracked)
            .HasColumnName("is_lot_tracked")
            .HasDefaultValue(false);

        builder.Property(i => i.IsSerialized)
            .HasColumnName("is_serialized")
            .HasDefaultValue(false);

        builder.Property(i => i.RequiresExpiryDate)
            .HasColumnName("requires_expiry_date")
            .HasDefaultValue(false);

        builder.Property(i => i.ShelfLifeDays)
            .HasColumnName("shelf_life_days")
            .HasDefaultValue(0);

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Soft delete & auditing
        builder.Property(i => i.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");
        builder.Property(i => i.DeletedBy).HasColumnName("deleted_by");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.CreatedBy).HasColumnName("created_by");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(i => i.ClientId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.AbcVelocity);

        // Relationships
        builder.HasOne(i => i.Client)
            .WithMany()
            .HasForeignKey(i => i.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

