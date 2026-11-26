using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class FxRateRecordConfiguration : IEntityTypeConfiguration<FxRateRecord>
{
    public void Configure(EntityTypeBuilder<FxRateRecord> builder)
    {
        builder.ToTable("fx_rates", t =>
        {
            t.HasCheckConstraint(
                "ck_fx_rates_distinct_currencies",
                "base_currency_code <> quote_currency_code");
            t.HasCheckConstraint(
                "ck_fx_rates_window",
                "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
            t.HasCheckConstraint(
                "ck_fx_rates_unit_multiplier_positive",
                "unit_multiplier > 0");
            t.HasCheckConstraint(
                "ck_fx_rates_positive_rates",
                "buy_rate > 0 AND sell_rate > 0 AND mid_rate > 0 AND buy_rate <= sell_rate");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("fx_rate_id")
            .ValueGeneratedNever();

        builder.Property(r => r.BaseCurrencyCode)
            .HasColumnName("base_currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.QuoteCurrencyCode)
            .HasColumnName("quote_currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(r => r.EffectiveToUtc)
            .HasColumnName("effective_to_utc");

        builder.Property(r => r.UnitMultiplier)
            .HasColumnName("unit_multiplier")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(r => r.BuyRate)
            .HasColumnName("buy_rate")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(r => r.SellRate)
            .HasColumnName("sell_rate")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(r => r.MidRate)
            .HasColumnName("mid_rate")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(r => r.RateDirection)
            .HasColumnName("rate_direction")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // String-converted to match PostgreSQL EXCLUDE USING gist predicate directly.
        builder.Property(r => r.SourceAuthority)
            .HasColumnName("source_authority")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.BulletinVersion)
            .HasColumnName("bulletin_version")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(r => r.BenchmarkSeriesId)
            .HasColumnName("benchmark_series_id")
            .HasMaxLength(80);

        builder.Property(r => r.IntegrityHash)
            .HasColumnName("integrity_hash")
            .HasMaxLength(128);

        builder.Property(r => r.EvidenceReference)
            .HasColumnName("evidence_reference")
            .HasMaxLength(400);

        builder.Property(r => r.TransformationTrace)
            .HasColumnName("transformation_trace")
            .HasMaxLength(400);

        builder.Property(r => r.PublishedAtUtc)
            .HasColumnName("published_at_utc")
            .IsRequired();

        builder.Property(r => r.ApprovalStatus)
            .HasColumnName("approval_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(r => r.ApprovedAtUtc).HasColumnName("approved_at_utc");
        builder.Property(r => r.VerificationNotes).HasColumnName("verification_notes").HasMaxLength(500);

        builder.Property(r => r.SupersededByRateId).HasColumnName("superseded_by_rate_id");
        builder.Property(r => r.SupersededAtUtc).HasColumnName("superseded_at_utc");

        builder.Property(r => r.VoidReason).HasColumnName("void_reason").HasMaxLength(500);
        builder.Property(r => r.VoidedByUserId).HasColumnName("voided_by_user_id");
        builder.Property(r => r.VoidedAtUtc).HasColumnName("voided_at_utc");

        builder.Property(r => r.ConfigRevision)
            .HasColumnName("config_revision")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(r => new { r.BaseCurrencyCode, r.QuoteCurrencyCode, r.SourceAuthority, r.ApprovalStatus, r.IsActive });

        builder.HasOne(r => r.BaseCurrency)
            .WithMany()
            .HasForeignKey(r => r.BaseCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.QuoteCurrency)
            .WithMany()
            .HasForeignKey(r => r.QuoteCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.SupersededByRate)
            .WithMany()
            .HasForeignKey(r => r.SupersededByRateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
