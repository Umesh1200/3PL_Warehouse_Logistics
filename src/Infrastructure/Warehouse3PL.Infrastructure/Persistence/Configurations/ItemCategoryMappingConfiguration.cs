using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ItemCategoryMappingConfiguration : IEntityTypeConfiguration<ItemCategoryMapping>
{
    public void Configure(EntityTypeBuilder<ItemCategoryMapping> builder)
    {
        builder.ToTable("item_category_mappings");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("map_id")
            .ValueGeneratedOnAdd();

        builder.Property(m => m.ItemId)
            .HasColumnName("item_id")
            .IsRequired();

        builder.Property(m => m.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(m => new { m.ItemId, m.CategoryId }).IsUnique();

        builder.HasOne(m => m.Item)
            .WithMany(i => i.CategoryMappings)
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Category)
            .WithMany(c => c.CategoryMappings)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

