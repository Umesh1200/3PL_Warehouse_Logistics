using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientAccessorialRuleConfiguration : IEntityTypeConfiguration<ClientAccessorialRule>
{
    public void Configure(EntityTypeBuilder<ClientAccessorialRule> builder)
    {
        builder.ToTable("client_accessorial_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("accessorial_rule_id")
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(r => r.ClientFreightAgreementId)
            .HasColumnName("client_freight_agreement_id")
            .IsRequired();

        builder.Property(r => r.AccessorialType)
            .HasColumnName("accessorial_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Treatment)
            .HasColumnName("treatment")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.MarkupPercentage)
            .HasColumnName("markup_percentage")
            .HasPrecision(18, 4);

        builder.Property(r => r.FixedFee)
            .HasColumnName("fixed_fee")
            .HasPrecision(18, 4);

        // Auditing
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(r => new { r.ClientFreightAgreementId, r.AccessorialType })
            .HasDatabaseName("ix_client_accessorial_rules_agreement_type")
            .IsUnique();

        // Relationships
        builder.HasOne(r => r.ClientFreightAgreement)
            .WithMany(a => a.AccessorialRules)
            .HasForeignKey(r => r.ClientFreightAgreementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

