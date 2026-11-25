using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class AdministrativeDivisionConfiguration : IEntityTypeConfiguration<AdministrativeDivision>
{
    public void Configure(EntityTypeBuilder<AdministrativeDivision> builder)
    {
        builder.ToTable("administrative_divisions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("division_id")
            .ValueGeneratedNever();

        builder.Property(d => d.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(d => d.ParentDivisionId)
            .HasColumnName("parent_division_id");

        builder.Property(d => d.DivisionLevel)
            .HasColumnName("division_level")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.IsoCode)
            .HasColumnName("iso_code")
            .HasMaxLength(20);

        builder.Property(d => d.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(d => d.LocalName)
            .HasColumnName("local_name")
            .HasMaxLength(120);

        builder.Property(d => d.PostalPrefix)
            .HasColumnName("postal_prefix")
            .HasMaxLength(20);

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(d => new { d.CountryCode, d.DivisionLevel, d.Code })
            .IsUnique();

        builder.HasIndex(d => new { d.CountryCode, d.ParentDivisionId });

        builder.HasIndex(d => d.IsoCode)
            .IsUnique()
            .HasFilter("iso_code IS NOT NULL");

        builder.HasOne(d => d.CountryProfile)
            .WithMany(c => c.Divisions)
            .HasForeignKey(d => d.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.ParentDivision)
            .WithMany(p => p.ChildDivisions)
            .HasForeignKey(d => d.ParentDivisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
