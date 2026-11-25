using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class CountryCompliancePolicyConfiguration : IEntityTypeConfiguration<CountryCompliancePolicy>
{
    public void Configure(EntityTypeBuilder<CountryCompliancePolicy> builder)
    {
        builder.ToTable("country_compliance_policies", t =>
        {
            t.HasCheckConstraint(
                "ck_country_compliance_policies_window",
                "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
            t.HasCheckConstraint(
                "ck_country_compliance_policies_version_positive",
                "version >= 1");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.CountryCode)
            .HasColumnName("country_code")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(p => p.PolicyCode)
            .HasColumnName("policy_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(p => p.Title)
            .HasColumnName("title")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(p => p.StatutorySourceRef)
            .HasColumnName("statutory_source_ref")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.OfficialNoticeCitation)
            .HasColumnName("official_notice_citation")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(p => p.ApplicabilityPredicateJson)
            .HasColumnName("applicability_predicate_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(p => p.EnforcementMode)
            .HasColumnName("enforcement_mode")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.EffectiveFromUtc)
            .HasColumnName("effective_from_utc")
            .IsRequired();

        builder.Property(p => p.EffectiveToUtc)
            .HasColumnName("effective_to_utc");

        // Stored as string ('Draft', 'VerifiedApproved', 'Superseded') to match PostgreSQL EXCLUDE USING gist predicate.
        builder.Property(p => p.ApprovalStatus)
            .HasColumnName("approval_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.ApprovedByUserId)
            .HasColumnName("approved_by_user_id");

        builder.Property(p => p.ApprovedAtUtc)
            .HasColumnName("approved_at_utc");

        builder.Property(p => p.VerificationEvidenceNotes)
            .HasColumnName("verification_evidence_notes")
            .HasMaxLength(500);

        builder.Property(p => p.SupersededByPolicyId)
            .HasColumnName("superseded_by_policy_id");

        builder.Property(p => p.SupersededAtUtc)
            .HasColumnName("superseded_at_utc");

        builder.Property(p => p.ConfigRevision)
            .HasColumnName("config_revision")
            .IsConcurrencyToken()
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => new { p.CountryCode, p.PolicyCode, p.Version })
            .IsUnique();

        builder.HasIndex(p => new { p.CountryCode, p.PolicyCode, p.ApprovalStatus, p.IsActive });

        builder.HasOne(p => p.CountryProfile)
            .WithMany(c => c.CompliancePolicies)
            .HasForeignKey(p => p.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.SupersededByPolicy)
            .WithMany()
            .HasForeignKey(p => p.SupersededByPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
