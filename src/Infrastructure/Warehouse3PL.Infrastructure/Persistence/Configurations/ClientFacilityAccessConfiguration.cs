using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientFacilityAccessConfiguration : IEntityTypeConfiguration<ClientFacilityAccess>
{
    public void Configure(EntityTypeBuilder<ClientFacilityAccess> builder)
    {
        builder.ToTable("client_facility_access");

        builder.HasKey(cfa => cfa.Id);
        builder.Property(cfa => cfa.Id)
            .HasColumnName("access_id")
            .ValueGeneratedOnAdd();

        builder.Property(cfa => cfa.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        // B-Tree index on client_id per DB-SPEC-3PL-2026
        builder.HasIndex(cfa => cfa.ClientId);

        builder.Property(cfa => cfa.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        // B-Tree index on facility_id per DB-SPEC-3PL-2026
        builder.HasIndex(cfa => cfa.FacilityId);

        builder.Property(cfa => cfa.EffectiveDate)
            .HasColumnName("effective_date")
            .IsRequired();

        builder.Property(cfa => cfa.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // B-Tree index on status per DB-SPEC-3PL-2026
        builder.HasIndex(cfa => cfa.Status);

        builder.Property(cfa => cfa.PrimaryOperatingFacility)
            .HasColumnName("primary_operating_facility")
            .HasDefaultValue(false);

        builder.Property(cfa => cfa.RevokedAtUtc)
            .HasColumnName("revoked_at_utc");

        builder.Property(cfa => cfa.Remarks)
            .HasColumnName("remarks")
            .HasMaxLength(500);

        builder.Property(cfa => cfa.ConfigRevision)
            .HasColumnName("config_revision")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(cfa => cfa.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(cfa => cfa.CreatedBy).HasColumnName("created_by");
        builder.Property(cfa => cfa.UpdatedAt).HasColumnName("updated_at");
        builder.Property(cfa => cfa.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(cfa => new { cfa.ClientId, cfa.FacilityId })
            .IsUnique();

        builder.HasIndex(cfa => cfa.ClientId)
            .IsUnique()
            .HasDatabaseName("ux_client_facility_access_primary_active")
            .HasFilter("primary_operating_facility = TRUE AND status = 'Active'");
    }
}

