using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RateCardTierConfiguration : IEntityTypeConfiguration<RateCardTier>
{
    public void Configure(EntityTypeBuilder<RateCardTier> builder)
    {
        builder.ToTable("rate_card_tiers");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("tier_id")
            .ValueGeneratedNever();

        builder.Property(t => t.RateCardLineId)
            .HasColumnName("rate_card_line_id")
            .IsRequired();

        builder.Property(t => t.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(t => t.TierIndex)
            .HasColumnName("tier_index")
            .IsRequired();

        builder.Property(t => t.TierMinQuantity)
            .HasColumnName("tier_min_quantity")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(t => t.TierMaxQuantity)
            .HasColumnName("tier_max_quantity")
            .HasPrecision(18, 4);

        builder.Property(t => t.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(18, 4)
            .IsRequired();

        // Auditing
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(t => new { t.RateCardLineId, t.TierIndex })
            .HasDatabaseName("ix_rate_card_tiers_line_index")
            .IsUnique();

        // Relationships
        builder.HasOne(t => t.RateCardLine)
            .WithMany(l => l.Tiers)
            .HasForeignKey(t => t.RateCardLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

