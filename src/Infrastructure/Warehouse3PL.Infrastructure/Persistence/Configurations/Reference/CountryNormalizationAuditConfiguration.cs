using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations.Reference;

public sealed class CountryNormalizationAuditConfiguration : IEntityTypeConfiguration<CountryNormalizationAudit>
{
    public void Configure(EntityTypeBuilder<CountryNormalizationAudit> builder)
    {
        builder.ToTable("country_normalization_audits");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("audit_id").ValueGeneratedNever();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(a => a.FieldName).HasColumnName("field_name").HasMaxLength(64).IsRequired();
        builder.Property(a => a.OriginalCountryValue).HasColumnName("original_country_value").HasMaxLength(120);
        builder.Property(a => a.NormalizedCountryCode).HasColumnName("normalized_country_code").HasMaxLength(3);
        builder.Property(a => a.NormalizationStatus)
            .HasColumnName("normalization_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(a => a.PreservedHistoricalValue).HasColumnName("preserved_historical_value").IsRequired();
        builder.Property(a => a.WasInferredFromParent).HasColumnName("was_inferred_from_parent").IsRequired();
        builder.Property(a => a.InferenceSource).HasColumnName("inference_source").HasMaxLength(120);
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(a => a.FacilityId).HasColumnName("facility_id");
        builder.Property(a => a.ClientId).HasColumnName("client_id");
        builder.Property(a => a.CorrelationBatchId).HasColumnName("correlation_batch_id").HasMaxLength(64).IsRequired();
        builder.Property(a => a.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();

        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.FieldName })
            .IsUnique()
            .HasDatabaseName("ux_country_normalization_audits_entity_field");
        builder.HasIndex(a => new { a.NormalizationStatus, a.FacilityId, a.EntityType })
            .HasDatabaseName("ix_country_normalization_audits_status_facility_type");
        builder.HasIndex(a => new { a.ClientId, a.NormalizationStatus })
            .HasDatabaseName("ix_country_normalization_audits_client_status");
    }
}
