using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Versioned country statutory compliance policy definition.
/// Seeded or newly created versions start strictly in <see cref="CompliancePolicyApprovalStatus.Draft"/>
/// with <see cref="IsActive"/> = false and NEVER enforce until verified Staff approval succeeds.
/// Published and superseded versions are immutable and preserved for historical auditability.
/// </summary>
public class CountryCompliancePolicy : AuditableEntity
{
    private static readonly string[] DisallowedPlaceholderMarkers =
    [
        "TBD",
        "TODO",
        "N/A",
        "NONE",
        "PLACEHOLDER",
        "PENDING_"
    ];

    public string CountryCode { get; set; } = string.Empty;
    public string PolicyCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string StatutorySourceRef { get; set; } = string.Empty;
    public string OfficialNoticeCitation { get; set; } = string.Empty;
    public string ApplicabilityPredicateJson { get; set; } = string.Empty;
    public ComplianceEnforcementMode EnforcementMode { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public CompliancePolicyApprovalStatus ApprovalStatus { get; set; } = CompliancePolicyApprovalStatus.Draft;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? VerificationEvidenceNotes { get; set; }
    public Guid? SupersededByPolicyId { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public long ConfigRevision { get; set; } = 1;
    public bool IsActive { get; set; }

    public CountryProfile CountryProfile { get; set; } = null!;
    public CountryCompliancePolicy? SupersededByPolicy { get; set; }

    public static CountryCompliancePolicy CreateDraft(
        string countryCode,
        string policyCode,
        int version,
        string title,
        string statutorySourceRef,
        string officialNoticeCitation,
        string applicabilityPredicateJson,
        ComplianceEnforcementMode enforcementMode,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        Guid? id = null)
    {
        var normalizedCountry = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedPolicy = (policyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedCountry.Length != 2 || !normalizedCountry.All(char.IsAsciiLetterUpper))
            throw new DomainException("COMPLIANCE_INVALID_COUNTRY", "CountryCode must be a 2-letter uppercase ISO 3166-1 alpha-2 code.");
        if (string.IsNullOrWhiteSpace(normalizedPolicy) || normalizedPolicy.Length > 50)
            throw new DomainException("COMPLIANCE_INVALID_POLICY_CODE", "PolicyCode is required and must not exceed 50 characters.");
        if (version < 1)
            throw new DomainException("COMPLIANCE_INVALID_VERSION", "Policy version must be >= 1.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("COMPLIANCE_INVALID_TITLE", "Policy title is required.");

        var fromUtc = NormalizeUtc(effectiveFromUtc);
        DateTime? toUtc = effectiveToUtc.HasValue ? NormalizeUtc(effectiveToUtc.Value) : null;
        if (toUtc.HasValue && toUtc.Value <= fromUtc)
            throw new DomainException("COMPLIANCE_INVALID_EFFECTIVE_WINDOW", "EffectiveToUtc must be strictly greater than EffectiveFromUtc.");

        var entity = new CountryCompliancePolicy
        {
            CountryCode = normalizedCountry,
            PolicyCode = normalizedPolicy,
            Version = version,
            Title = title.Trim(),
            StatutorySourceRef = (statutorySourceRef ?? string.Empty).Trim(),
            OfficialNoticeCitation = (officialNoticeCitation ?? string.Empty).Trim(),
            ApplicabilityPredicateJson = (applicabilityPredicateJson ?? string.Empty).Trim(),
            EnforcementMode = enforcementMode,
            EffectiveFromUtc = fromUtc,
            EffectiveToUtc = toUtc,
            ApprovalStatus = CompliancePolicyApprovalStatus.Draft,
            ApprovedByUserId = null,
            ApprovedAtUtc = null,
            VerificationEvidenceNotes = null,
            SupersededByPolicyId = null,
            SupersededAtUtc = null,
            ConfigRevision = 1,
            IsActive = false
        };

        if (id.HasValue && id.Value != Guid.Empty)
        {
            entity.Id = id.Value;
        }

        return entity;
    }

    public bool OverlapsWindow(DateTime otherFromUtc, DateTime? otherToUtc)
    {
        var aStart = EffectiveFromUtc;
        var aEnd = EffectiveToUtc ?? DateTime.MaxValue;
        var bStart = NormalizeUtc(otherFromUtc);
        var bEnd = otherToUtc.HasValue ? NormalizeUtc(otherToUtc.Value) : DateTime.MaxValue;
        return aStart < bEnd && bStart < aEnd;
    }

    public bool CoversInstant(DateTime utcInstant)
    {
        var utc = NormalizeUtc(utcInstant);
        return utc >= EffectiveFromUtc && (!EffectiveToUtc.HasValue || utc < EffectiveToUtc.Value);
    }

    /// <summary>
    /// Transitions a Draft policy to VerifiedApproved after validating non-placeholder statutory provenance,
    /// verified Staff approver identity, effective window bounds, and explicit verification evidence notes.
    /// </summary>
    public void Approve(
        Guid verifiedStaffUserId,
        DateTime approvedAtUtc,
        string verificationEvidenceNotes,
        string? statutorySourceRefOverride = null,
        string? officialNoticeCitationOverride = null)
    {
        if (ApprovalStatus != CompliancePolicyApprovalStatus.Draft || IsActive)
        {
            throw new ConcurrencyException(
                "COMPLIANCE_POLICY_NOT_DRAFT",
                $"Compliance policy '{PolicyCode}' v{Version} is already '{ApprovalStatus}' and cannot be re-approved.");
        }

        if (verifiedStaffUserId == Guid.Empty)
        {
            throw new DomainException("COMPLIANCE_INVALID_APPROVER", "Verified Staff approver UserId is required.");
        }

        if (!string.IsNullOrWhiteSpace(statutorySourceRefOverride))
        {
            StatutorySourceRef = statutorySourceRefOverride.Trim();
        }

        if (!string.IsNullOrWhiteSpace(officialNoticeCitationOverride))
        {
            OfficialNoticeCitation = officialNoticeCitationOverride.Trim();
        }

        ValidateStatutoryEvidence(StatutorySourceRef, nameof(StatutorySourceRef));
        ValidateStatutoryEvidence(OfficialNoticeCitation, nameof(OfficialNoticeCitation));

        if (string.IsNullOrWhiteSpace(ApplicabilityPredicateJson))
        {
            throw new DomainException(
                "COMPLIANCE_MISSING_PREDICATE",
                "ApplicabilityPredicateJson is mandatory before policy approval.");
        }

        if (string.IsNullOrWhiteSpace(verificationEvidenceNotes) || verificationEvidenceNotes.Trim().Length < 8)
        {
            throw new DomainException(
                "COMPLIANCE_MISSING_VERIFICATION_EVIDENCE",
                "Explicit VerificationEvidenceNotes (>= 8 characters) confirming provenance review are required for statutory approval.");
        }

        if (EffectiveToUtc.HasValue && EffectiveToUtc.Value <= EffectiveFromUtc)
        {
            throw new DomainException(
                "COMPLIANCE_INVALID_EFFECTIVE_WINDOW",
                "EffectiveToUtc must be strictly greater than EffectiveFromUtc.");
        }

        ApprovalStatus = CompliancePolicyApprovalStatus.VerifiedApproved;
        IsActive = true;
        ApprovedByUserId = verifiedStaffUserId;
        ApprovedAtUtc = NormalizeUtc(approvedAtUtc);
        VerificationEvidenceNotes = verificationEvidenceNotes.Trim();
        ConfigRevision++;
    }

    /// <summary>
    /// Atomically transitions an active VerifiedApproved policy to Superseded while preserving audit provenance.
    /// </summary>
    public void Supersede(Guid replacementPolicyId, DateTime supersededAtUtc)
    {
        if (ApprovalStatus != CompliancePolicyApprovalStatus.VerifiedApproved || !IsActive)
        {
            throw new ConcurrencyException(
                "COMPLIANCE_POLICY_NOT_ACTIVE",
                $"Compliance policy '{PolicyCode}' v{Version} is not active VerifiedApproved and cannot be superseded.");
        }

        if (replacementPolicyId == Guid.Empty || replacementPolicyId == Id)
        {
            throw new DomainException("COMPLIANCE_INVALID_SUPERSESSION", "Valid replacementPolicyId is required to supersede a policy.");
        }

        ApprovalStatus = CompliancePolicyApprovalStatus.Superseded;
        IsActive = false;
        SupersededByPolicyId = replacementPolicyId;
        SupersededAtUtc = NormalizeUtc(supersededAtUtc);
        ConfigRevision++;
    }

    private static void ValidateStatutoryEvidence(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 5)
        {
            throw new DomainException(
                "COMPLIANCE_MISSING_STATUTORY_EVIDENCE",
                $"{fieldName} must contain substantive statutory provenance before approval.");
        }

        var upper = value.Trim().ToUpperInvariant();
        foreach (var marker in DisallowedPlaceholderMarkers)
        {
            if (upper == marker || upper.StartsWith(marker, StringComparison.Ordinal) || upper.Contains("PENDING_VERIFIED", StringComparison.Ordinal))
            {
                throw new DomainException(
                    "COMPLIANCE_PLACEHOLDER_EVIDENCE_REJECTED",
                    $"{fieldName} contains unverified placeholder text ('{value}') and cannot be published.");
            }
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => throw new DomainException(
                "COMPLIANCE_UNSPECIFIED_TIMESTAMP_KIND",
                "Timestamps must explicitly specify UTC (DateTimeKind.Utc) or a timezone offset (DateTimeKind.Local). Unspecified DateTimeKind is rejected fail-closed.")
        };
}
