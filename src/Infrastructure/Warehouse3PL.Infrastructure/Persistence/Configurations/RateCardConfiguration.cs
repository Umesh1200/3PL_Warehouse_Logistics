using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("rate_cards");

        builder.HasKey(rc => rc.Id);
        builder.Property(rc => rc.Id)
            .HasColumnName("rate_card_id")
            .ValueGeneratedNever();

        builder.Property(rc => rc.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(rc => rc.RateCardCode)
            .HasColumnName("rate_card_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rc => rc.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(rc => rc.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(rc => rc.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(rc => rc.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rc => rc.DefaultFacilityId)
            .HasColumnName("default_facility_id");

        // Auditing
        builder.Property(rc => rc.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(rc => rc.CreatedBy).HasColumnName("created_by");
        builder.Property(rc => rc.UpdatedAt).HasColumnName("updated_at");
        builder.Property(rc => rc.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(rc => new { rc.ClientId, rc.RateCardCode })
            .HasDatabaseName("ix_rate_cards_client_code")
            .IsUnique();

        builder.HasIndex(rc => new { rc.ClientId, rc.Status })
            .HasDatabaseName("ix_rate_cards_client_status");

        // Relationships
        builder.HasOne(rc => rc.Client)
            .WithMany()
            .HasForeignKey(rc => rc.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rc => rc.DefaultFacility)
            .WithMany()
            .HasForeignKey(rc => rc.DefaultFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(rc => rc.Schedules)
            .WithOne(s => s.RateCard)
            .HasForeignKey(s => s.RateCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

