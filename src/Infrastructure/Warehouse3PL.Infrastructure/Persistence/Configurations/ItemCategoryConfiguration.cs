using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> builder)
    {
        builder.ToTable("item_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("category_id")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.ParentCategoryId)
            .HasColumnName("parent_category_id");

        builder.Property(c => c.CategoryName)
            .HasColumnName("category_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.TaxCode)
            .HasColumnName("tax_code")
            .HasMaxLength(50);

        builder.Property(c => c.StorageAttributeFlag)
            .HasColumnName("storage_attribute_flag")
            .HasDefaultValue(false);

        // Self-referencing relationship
        builder.HasOne(c => c.ParentCategory)
            .WithMany(p => p.ChildCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

