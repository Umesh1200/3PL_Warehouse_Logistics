using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("invoice_line_id")
            .ValueGeneratedNever();

        builder.Property(l => l.InvoiceId)
            .HasColumnName("invoice_id")
            .IsRequired();

        builder.Property(l => l.LineNumber)
            .HasColumnName("line_number")
            .IsRequired();

        builder.Property(l => l.ChargeCategory)
            .HasColumnName("charge_category")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(l => l.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.UnitOfMeasure)
            .HasColumnName("unit_of_measure")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(l => l.BillableActivityId)
            .HasColumnName("billable_activity_id");

        builder.Property(l => l.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(l => l.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(l => l.MetadataJson)
            .HasColumnName("metadata_json");

        // Auditing
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.CreatedBy).HasColumnName("created_by");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.Property(l => l.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(l => new { l.InvoiceId, l.LineNumber })
            .HasDatabaseName("ix_invoice_lines_invoice_number")
            .IsUnique();

        builder.HasIndex(l => l.BillableActivityId)
            .HasDatabaseName("ix_invoice_lines_activity");

        // Relationships
        builder.HasOne(l => l.Invoice)
            .WithMany(i => i.Lines)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.BillableActivity)
            .WithMany()
            .HasForeignKey(l => l.BillableActivityId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

