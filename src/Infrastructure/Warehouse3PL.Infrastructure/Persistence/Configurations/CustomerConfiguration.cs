using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("customer_id").ValueGeneratedNever();

        builder.Property(c => c.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(c => c.CustomerCode).HasColumnName("customer_code").HasMaxLength(50).IsRequired();
        builder.Property(c => c.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.CustomerType).HasColumnName("customer_type").HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(c => c.ContactName).HasColumnName("contact_name").HasMaxLength(100);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(50);

        builder.Property(c => c.DefaultAddressLine1).HasColumnName("default_address_line1").HasMaxLength(200);
        builder.Property(c => c.DefaultAddressLine2).HasColumnName("default_address_line2").HasMaxLength(200);
        builder.Property(c => c.DefaultCity).HasColumnName("default_city").HasMaxLength(100);
        builder.Property(c => c.DefaultStateOrProvince).HasColumnName("default_state_or_province").HasMaxLength(100);
        builder.Property(c => c.DefaultPostalCode).HasColumnName("default_postal_code").HasMaxLength(30);
        builder.Property(c => c.DefaultCountryCode).HasColumnName("default_country_code").HasMaxLength(10);
        builder.Property(c => c.CountryNormalizationStatus)
            .HasColumnName("country_normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CountryNormalizationStatus.PendingNormalization)
            .IsRequired();

        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(c => c.Version).HasColumnName("version").IsConcurrencyToken();

        // Auditing
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Soft delete
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
        builder.Property(c => c.DeletedBy).HasColumnName("deleted_by");
        builder.HasQueryFilter(c => !c.IsDeleted);

        // Indexes
        builder.HasIndex(c => new { c.ClientId, c.CustomerCode }).IsUnique();
        builder.HasIndex(c => new { c.ClientId, c.CustomerName });

        // Relationships
        builder.HasOne(c => c.Client).WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Orders).WithOne(o => o.Customer).HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.SetNull);
    }
}

