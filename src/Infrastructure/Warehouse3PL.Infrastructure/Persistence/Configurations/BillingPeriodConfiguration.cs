using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class BillingPeriodConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> builder)
    {
        builder.ToTable("billing_periods");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("period_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(p => p.PeriodCode)
            .HasColumnName("period_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.StartDateUtc)
            .HasColumnName("start_date_utc")
            .IsRequired();

        builder.Property(p => p.EndDateUtc)
            .HasColumnName("end_date_utc")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Frequency)
            .HasColumnName("frequency")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.LockedAtUtc)
            .HasColumnName("locked_at_utc");

        builder.Property(p => p.ClosedAtUtc)
            .HasColumnName("closed_at_utc");

        builder.Property(p => p.InvoicedAtUtc)
            .HasColumnName("invoiced_at_utc");

        builder.Property(p => p.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        // Auditing
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(p => new { p.ClientId, p.PeriodCode })
            .HasDatabaseName("ix_billing_periods_client_code")
            .IsUnique();

        builder.HasIndex(p => new { p.ClientId, p.StartDateUtc, p.EndDateUtc })
            .HasDatabaseName("ix_billing_periods_client_dates");

        builder.HasIndex(p => new { p.ClientId, p.Status })
            .HasDatabaseName("ix_billing_periods_client_status");

        // Relationships
        builder.HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Facility)
            .WithMany()
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Activities)
            .WithOne(a => a.BillingPeriod)
            .HasForeignKey(a => a.BillingPeriodId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

