using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Effective-dated, supersession-tracked foreign exchange rate record anchored to ISO 4217 <see cref="CurrencyMaster"/>.
/// Supports multi-unit central bank quotations such as Nepal Rastra Bank 100 INR -> NPR (<c>UnitMultiplier = 100</c>),
/// strict publication look-ahead prevention (<c>PublishedAtUtc</c>), and non-overlapping active windows.
/// Retroactive mutation or physical deletion of approved history is prohibited.
/// </summary>
public class FxRateRecord : AuditableEntity<Guid>
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;

    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }

    /// <summary>
    /// Number of BaseCurrency units quoted by BuyRate/SellRate/MidRate (e.g. 100 for INR->NPR, 1 for USD->NPR).
    /// Conversion factor per 1 BaseCurrency = SelectedRate / UnitMultiplier.
    /// </summary>
    public int UnitMultiplier { get; set; } = 1;

    public decimal BuyRate { get; set; }
    public decimal SellRate { get; set; }
    public decimal MidRate { get; set; }

    public FxRateDirection RateDirection { get; set; } = FxRateDirection.QuotePerUnitBase;
    public FxRateSourceAuthority SourceAuthority { get; set; }

    public string BulletinVersion { get; set; } = string.Empty;
    public string? BenchmarkSeriesId { get; set; }
    public string? IntegrityHash { get; set; }
    public string? EvidenceReference { get; set; }
    public string? TransformationTrace { get; set; }
    public DateTime PublishedAtUtc { get; set; }

    public FxRateApprovalStatus ApprovalStatus { get; set; } = FxRateApprovalStatus.Draft;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? VerificationNotes { get; set; }

    public Guid? SupersededByRateId { get; set; }
    public DateTime? SupersededAtUtc { get; set; }

    public string? VoidReason { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }

    public long ConfigRevision { get; set; } = 1;
    public bool IsActive { get; set; }

    public CurrencyMaster BaseCurrency { get; set; } = null!;
    public CurrencyMaster QuoteCurrency { get; set; } = null!;
    public FxRateRecord? SupersededByRate { get; set; }

    public FxRateRecord()
    {
        Id = Guid.NewGuid();
    }

    public static FxRateRecord CreateDraft(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        int unitMultiplier,
        decimal buyRate,
        decimal sellRate,
        decimal midRate,
        FxRateSourceAuthority sourceAuthority,
        string bulletinVersion,
        DateTime publishedAtUtc,
        string? benchmarkSeriesId = null,
        string? integrityHash = null,
        FxRateDirection rateDirection = FxRateDirection.QuotePerUnitBase,
        Guid? id = null,
        string? evidenceReference = null,
        string? transformationTrace = null)
    {
        var normalizedBase = ValidateIsoCurrency(baseCurrencyCode, nameof(baseCurrencyCode));
        var normalizedQuote = ValidateIsoCurrency(quoteCurrencyCode, nameof(quoteCurrencyCode));

        if (string.Equals(normalizedBase, normalizedQuote, StringComparison.Ordinal))
        {
            throw new DomainException(
                "FX rate record requires distinct BaseCurrencyCode and QuoteCurrencyCode.",
                "FX_RATE_SAME_CURRENCY_PAIR");
        }

        if (effectiveFromUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("EffectiveFromUtc must explicitly use DateTimeKind.Utc.", "FX_RATE_INVALID_EFFECTIVE_KIND");
        }

        if (effectiveToUtc.HasValue)
        {
            if (effectiveToUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new DomainException("EffectiveToUtc must explicitly use DateTimeKind.Utc.", "FX_RATE_INVALID_EFFECTIVE_KIND");
            }

            if (effectiveToUtc.Value <= effectiveFromUtc)
            {
                throw new DomainException("EffectiveToUtc must be strictly greater than EffectiveFromUtc.", "FX_RATE_INVALID_WINDOW");
            }
        }

        if (publishedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("PublishedAtUtc must explicitly use DateTimeKind.Utc.", "FX_RATE_INVALID_PUBLISHED_KIND");
        }

        if (unitMultiplier <= 0)
        {
            throw new DomainException("UnitMultiplier must be a positive integer (e.g. 1 or 100).", "FX_RATE_INVALID_MULTIPLIER");
        }

        if (buyRate <= 0m || sellRate <= 0m || midRate <= 0m)
        {
            throw new DomainException("BuyRate, SellRate, and MidRate must all be strictly positive.", "FX_RATE_NON_POSITIVE_RATE");
        }

        if (buyRate > sellRate)
        {
            throw new DomainException("BuyRate cannot exceed SellRate.", "FX_RATE_INVERTED_SPREAD");
        }

        if (string.IsNullOrWhiteSpace(bulletinVersion) || bulletinVersion.Trim().Length > 80)
        {
            throw new DomainException("BulletinVersion is required and must not exceed 80 characters.", "FX_RATE_MISSING_BULLETIN_VERSION");
        }

        var record = new FxRateRecord
        {
            BaseCurrencyCode = normalizedBase,
            QuoteCurrencyCode = normalizedQuote,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            UnitMultiplier = unitMultiplier,
            BuyRate = decimal.Round(buyRate, 6, MidpointRounding.AwayFromZero),
            SellRate = decimal.Round(sellRate, 6, MidpointRounding.AwayFromZero),
            MidRate = decimal.Round(midRate, 6, MidpointRounding.AwayFromZero),
            RateDirection = rateDirection,
            SourceAuthority = sourceAuthority,
            BulletinVersion = bulletinVersion.Trim(),
            BenchmarkSeriesId = string.IsNullOrWhiteSpace(benchmarkSeriesId) ? null : benchmarkSeriesId.Trim(),
            IntegrityHash = string.IsNullOrWhiteSpace(integrityHash) ? null : integrityHash.Trim(),
            EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim(),
            TransformationTrace = string.IsNullOrWhiteSpace(transformationTrace) ? null : transformationTrace.Trim(),
            PublishedAtUtc = publishedAtUtc,
            ApprovalStatus = FxRateApprovalStatus.Draft,
            ConfigRevision = 1,
            IsActive = false
        };

        if (id.HasValue && id.Value != Guid.Empty)
        {
            record.Id = id.Value;
        }

        return record;
    }

    public void Approve(
        Guid approvedByUserId,
        DateTime approvedAtUtc,
        string verificationNotes,
        string? verifiedBenchmarkSeriesId = null,
        string? verifiedIntegrityHash = null,
        string? evidenceReference = null,
        string? transformationTrace = null)
    {
        if (ApprovalStatus != FxRateApprovalStatus.Draft || IsActive)
        {
            throw new ConcurrencyException(
                "FX_RATE_ALREADY_PROCESSED",
                $"FX rate '{BulletinVersion}' is already '{ApprovalStatus}' and cannot be re-approved.");
        }

        if (approvedByUserId == Guid.Empty)
        {
            throw new DomainException("ApprovedByUserId is required.", "FX_RATE_MISSING_APPROVER");
        }

        if (string.IsNullOrWhiteSpace(verificationNotes))
        {
            throw new DomainException("VerificationNotes are required to approve an FX rate bulletin.", "FX_RATE_MISSING_VERIFICATION_NOTES");
        }

        if (!string.IsNullOrWhiteSpace(verifiedBenchmarkSeriesId))
        {
            BenchmarkSeriesId = verifiedBenchmarkSeriesId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(verifiedIntegrityHash))
        {
            IntegrityHash = verifiedIntegrityHash.Trim();
        }

        if (!string.IsNullOrWhiteSpace(evidenceReference))
        {
            EvidenceReference = evidenceReference.Trim();
        }

        if (!string.IsNullOrWhiteSpace(transformationTrace))
        {
            TransformationTrace = transformationTrace.Trim();
        }

        ApprovalStatus = FxRateApprovalStatus.VerifiedApproved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
        VerificationNotes = verificationNotes.Trim();
        IsActive = true;
        ConfigRevision++;
    }

    public void QuarantineUnverified(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Quarantine reason is required.", "FX_RATE_MISSING_QUARANTINE_REASON");
        }

        var priorStatus = ApprovalStatus;
        ApprovalStatus = FxRateApprovalStatus.Quarantined;
        IsActive = false;
        EvidenceReference = "QUARANTINED_UNVERIFIED_LEGACY";
        VerificationNotes = $"[QUARANTINED({priorStatus}): {reason.Trim()}] {VerificationNotes}".Trim();
        ConfigRevision++;
    }

    public void Supersede(Guid replacementRateId, DateTime supersededAtUtc)
    {
        if (ApprovalStatus != FxRateApprovalStatus.VerifiedApproved || !IsActive)
        {
            throw new ConcurrencyException(
                "FX_RATE_NOT_ACTIVE",
                $"Only active VerifiedApproved FX rates can be superseded (current: '{ApprovalStatus}').");
        }

        ApprovalStatus = FxRateApprovalStatus.Superseded;
        SupersededByRateId = replacementRateId;
        SupersededAtUtc = supersededAtUtc;
        IsActive = false;
        ConfigRevision++;
    }

    public void Void(Guid voidedByUserId, DateTime voidedAtUtc, string reason)
    {
        if (ApprovalStatus == FxRateApprovalStatus.Voided)
        {
            throw new ConcurrencyException("FX_RATE_ALREADY_VOIDED", $"FX rate '{BulletinVersion}' is already voided.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("VoidReason is required to void an FX rate record.", "FX_RATE_MISSING_VOID_REASON");
        }

        ApprovalStatus = FxRateApprovalStatus.Voided;
        VoidedByUserId = voidedByUserId;
        VoidedAtUtc = voidedAtUtc;
        VoidReason = reason.Trim();
        IsActive = false;
        ConfigRevision++;
    }

    public bool OverlapsWindow(DateTime candidateFromUtc, DateTime? candidateToUtc)
    {
        var thisEnd = EffectiveToUtc ?? DateTime.MaxValue;
        var otherEnd = candidateToUtc ?? DateTime.MaxValue;
        return EffectiveFromUtc < otherEnd && candidateFromUtc < thisEnd;
    }

    public decimal ComputeUnitRate(FxRateType rateType)
    {
        var rawRate = rateType switch
        {
            FxRateType.Buy => BuyRate,
            FxRateType.Sell => SellRate,
            FxRateType.Mid => MidRate,
            _ => throw new DomainException($"Unsupported FxRateType '{rateType}'.", "FX_RATE_INVALID_TYPE")
        };

        return rawRate / UnitMultiplier;
    }

    private static string ValidateIsoCurrency(string code, string paramName)
    {
        try
        {
            Iso4217Currencies.Validate(code, paramName);
            return code.Trim().ToUpperInvariant();
        }
        catch (ArgumentException ex)
        {
            throw new DomainException(ex.Message, "FX_RATE_INVALID_CURRENCY");
        }
    }
}
