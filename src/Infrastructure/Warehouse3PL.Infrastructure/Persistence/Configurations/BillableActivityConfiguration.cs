using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class BillableActivityConfiguration : IEntityTypeConfiguration<BillableActivity>
{
    public void Configure(EntityTypeBuilder<BillableActivity> builder)
    {
        builder.ToTable("billable_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("activity_id")
            .ValueGeneratedNever();

        builder.Property(a => a.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(a => a.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired();

        builder.Property(a => a.BillingPeriodId)
            .HasColumnName("billing_period_id");

        builder.Property(a => a.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.SourceId)
            .HasColumnName("source_id")
            .IsRequired();

        builder.Property(a => a.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(a => a.UnitOfMeasure)
            .HasColumnName("unit_of_measure")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(18, 4);

        builder.Property(a => a.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4);

        builder.Property(a => a.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(a => a.RateCardId)
            .HasColumnName("rate_card_id");

        builder.Property(a => a.RateCardScheduleId)
            .HasColumnName("rate_card_schedule_id");

        builder.Property(a => a.RateCardTierId)
            .HasColumnName("rate_card_tier_id");

        builder.Property(a => a.RateCardVersion)
            .HasColumnName("rate_card_version");

        builder.Property(a => a.RatedAtUtc)
            .HasColumnName("rated_at_utc");

        builder.Property(a => a.RatedByUserId)
            .HasColumnName("rated_by_user_id");

        builder.Property(a => a.InvoiceId)
            .HasColumnName("invoice_id");

        builder.Property(a => a.InvoiceLineId)
            .HasColumnName("invoice_line_id");

        builder.Property(a => a.InvoicedAtUtc)
            .HasColumnName("invoiced_at_utc");

        builder.Property(a => a.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(a => a.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.MetadataJson)
            .HasColumnName("metadata_json");

        // Auditing
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(a => new { a.ClientId, a.IdempotencyKey })
            .HasDatabaseName("ix_billable_activities_client_idempotency")
            .IsUnique();

        builder.HasIndex(a => new { a.ClientId, a.Status, a.OccurredAtUtc })
            .HasDatabaseName("ix_billable_activities_client_status");

        builder.HasIndex(a => new { a.ClientId, a.SourceType, a.SourceId })
            .HasDatabaseName("ix_billable_activities_source");

        builder.HasIndex(a => new { a.BillingPeriodId, a.Status })
            .HasDatabaseName("ix_billable_activities_period");

        // Relationships
        builder.HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Facility)
            .WithMany()
            .HasForeignKey(a => a.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.BillingPeriod)
            .WithMany(p => p.Activities)
            .HasForeignKey(a => a.BillingPeriodId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

