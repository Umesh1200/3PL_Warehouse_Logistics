using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ShippingLabelConfiguration : IEntityTypeConfiguration<ShippingLabel>
{
    public void Configure(EntityTypeBuilder<ShippingLabel> builder)
    {
        builder.ToTable("shipping_labels");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("label_id")
            .ValueGeneratedNever();

        builder.Property(l => l.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(l => l.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(l => l.CarrierCode)
            .HasColumnName("carrier_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.ServiceLevel)
            .HasColumnName("service_level")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.LabelFormat)
            .HasColumnName("label_format")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.LabelData)
            .HasColumnName("label_data")
            .IsRequired();

        builder.Property(l => l.RoutingCode)
            .HasColumnName("routing_code")
            .HasMaxLength(100);

        builder.Property(l => l.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.GeneratedAtUtc)
            .HasColumnName("generated_at_utc")
            .IsRequired();

        builder.Property(l => l.PrintedAtUtc)
            .HasColumnName("printed_at_utc");

        builder.Property(l => l.VoidedAtUtc)
            .HasColumnName("voided_at_utc");

        builder.Property(l => l.VoidReason)
            .HasColumnName("void_reason")
            .HasMaxLength(500);

        builder.Ignore(l => l.IsVoided);

        // Indexes
        builder.HasIndex(l => l.PackageId);
        builder.HasIndex(l => l.TrackingNumber);
        builder.HasIndex(l => l.Status);

        // Relationships
        builder.HasOne(l => l.Package)
            .WithMany(p => p.ShippingLabels)
            .HasForeignKey(l => l.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

