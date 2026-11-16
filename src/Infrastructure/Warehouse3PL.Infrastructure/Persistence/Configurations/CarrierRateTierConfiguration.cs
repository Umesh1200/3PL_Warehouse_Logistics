using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CarrierRateTierConfiguration : IEntityTypeConfiguration<CarrierRateTier>
{
    public void Configure(EntityTypeBuilder<CarrierRateTier> builder)
    {
        builder.ToTable("carrier_rate_tiers");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("carrier_rate_tier_id")
            .ValueGeneratedNever();

        builder.Property(t => t.CarrierRateAgreementId)
            .HasColumnName("carrier_rate_agreement_id")
            .IsRequired();

        builder.Property(t => t.TierNumber)
            .HasColumnName("tier_number")
            .IsRequired();

        builder.Property(t => t.LowerBound)
            .HasColumnName("lower_bound")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(t => t.UpperBound)
            .HasColumnName("upper_bound")
            .HasPrecision(18, 4);

        builder.Property(t => t.Rate)
            .HasColumnName("rate")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(t => t.UpdatedBy)
            .HasColumnName("updated_by");

        builder.HasIndex(t => new { t.CarrierRateAgreementId, t.TierNumber }).IsUnique();
    }
}
