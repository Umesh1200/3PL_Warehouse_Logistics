using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Shared bitemporal FX rate selector used identically by <see cref="FxConversionService"/>
/// and <see cref="Analytics.FxRateProvider"/> so both interfaces produce identical historical decisions:
/// 1) Never resolves Draft, Voided, or Quarantined rates (Voided records are retracted/erroneous bulletins;
///    and Quarantined/unverified legacy official records lack authenticated source evidence).
/// 2) Filters candidate rows by temporal availability timestamps BEFORE selecting a winner, preventing
///    a future-published authority from shadowing an already-published eligible authority.
/// 3) Distinguishes:
///    - <see cref="FxValuationMode.HistoricalAsKnown"/>: requires <c>PublishedAtUtc &lt;= asOfUtc</c>,
///      <c>ApprovedAtUtc &lt;= asOfUtc</c>, and <c>(SupersededAtUtc == null || asOfUtc &lt; SupersededAtUtc)</c>.
///    - <see cref="FxValuationMode.Retrospective"/>: includes <c>VerifiedApproved</c> and <c>Superseded</c>
///      rates published by <c>asOfUtc</c> regardless of internal approval chronology, preferring currently active rows.
/// 4) Applies deterministic precedence across eligible authorities:
///    most recent <c>EffectiveFromUtc</c>, then central-bank authority rank
///    (<c>NRB &gt; FRB &gt; ECB &gt; BOE &gt; ManualApproved</c>), then <c>PublishedAtUtc</c>, <c>ApprovedAtUtc</c>, <c>Id</c>.
/// </summary>
internal static class FxDatabaseRateResolver
{
    public static int GetAuthorityRank(FxRateSourceAuthority authority) => authority switch
    {
        FxRateSourceAuthority.NRB => 1,
        FxRateSourceAuthority.FRB => 2,
        FxRateSourceAuthority.ECB => 3,
        FxRateSourceAuthority.BOE => 4,
        FxRateSourceAuthority.ManualApproved => 5,
        _ => 99
    };

    public static async Task<FxRateRecord?> ResolveAsync(
        IApplicationDbContext db,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateTime effectiveDateUtc,
        DateTime asOfUtc,
        FxValuationMode valuationMode,
        FxRateSourceAuthority? preferredAuthority,
        CancellationToken cancellationToken = default)
    {
        var candidates = await QueryPublishedEffectiveWindow(
                db, baseCurrencyCode, quoteCurrencyCode, effectiveDateUtc, preferredAuthority)
            .ToListAsync(cancellationToken);

        return SelectBestCandidate(candidates, asOfUtc, valuationMode);
    }

    public static FxRateRecord? ResolveSync(
        IApplicationDbContext db,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateTime effectiveDateUtc,
        DateTime asOfUtc,
        FxValuationMode valuationMode,
        FxRateSourceAuthority? preferredAuthority)
    {
        var candidates = QueryPublishedEffectiveWindow(
                db, baseCurrencyCode, quoteCurrencyCode, effectiveDateUtc, preferredAuthority)
            .ToList();

        return SelectBestCandidate(candidates, asOfUtc, valuationMode);
    }

    internal static IQueryable<FxRateRecord> QueryAllPublishedForPair(
        IApplicationDbContext db,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        FxRateSourceAuthority? preferredAuthority)
    {
        var query = db.FxRateRecords
            .AsNoTracking()
            .Where(r => r.BaseCurrencyCode == baseCurrencyCode &&
                        r.QuoteCurrencyCode == quoteCurrencyCode &&
                        (r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved ||
                         r.ApprovalStatus == FxRateApprovalStatus.Superseded) &&
                        r.ApprovedByUserId != null &&
                        r.VerificationNotes != null &&
                        r.EvidenceReference != null &&
                        r.EvidenceReference != "" &&
                        r.EvidenceReference != "QUARANTINED_UNVERIFIED_LEGACY" &&
                        (r.SourceAuthority == FxRateSourceAuthority.ManualApproved ||
                         (r.EvidenceReference != "MANUAL_GOVERNANCE_OVERRIDE" &&
                          r.BenchmarkSeriesId != null &&
                          r.IntegrityHash != null &&
                          r.IntegrityHash.StartsWith("SHA256:"))));

        if (preferredAuthority.HasValue)
        {
            query = query.Where(r => r.SourceAuthority == preferredAuthority.Value);
        }

        return query;
    }

    internal static IQueryable<FxRateRecord> QueryPublishedEffectiveWindow(
        IApplicationDbContext db,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateTime effectiveDateUtc,
        FxRateSourceAuthority? preferredAuthority)
        => QueryAllPublishedForPair(db, baseCurrencyCode, quoteCurrencyCode, preferredAuthority)
            .Where(r => r.EffectiveFromUtc <= effectiveDateUtc &&
                        (r.EffectiveToUtc == null || effectiveDateUtc < r.EffectiveToUtc));

    private static bool HasEligibleProvenance(FxRateRecord r)
    {
        if (!r.ApprovedByUserId.HasValue || string.IsNullOrWhiteSpace(r.VerificationNotes))
            return false;

        if (string.IsNullOrWhiteSpace(r.EvidenceReference) ||
            string.Equals(r.EvidenceReference, "QUARANTINED_UNVERIFIED_LEGACY", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (r.SourceAuthority == FxRateSourceAuthority.ManualApproved)
            return true;

        return !string.Equals(r.EvidenceReference, "MANUAL_GOVERNANCE_OVERRIDE", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(r.BenchmarkSeriesId) &&
               !string.IsNullOrWhiteSpace(r.IntegrityHash) &&
               r.IntegrityHash.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase) &&
               r.IntegrityHash.Length == "SHA256:".Length + 64;
    }

    internal static FxRateRecord? SelectBestCandidate(
        IEnumerable<FxRateRecord> candidates,
        DateTime asOfUtc,
        FxValuationMode valuationMode)
    {
        var provenanceEligible = candidates.Where(HasEligibleProvenance);

        IEnumerable<FxRateRecord> eligible = valuationMode == FxValuationMode.HistoricalAsKnown
            ? provenanceEligible.Where(r =>
                r.PublishedAtUtc <= asOfUtc &&
                r.ApprovedAtUtc.HasValue &&
                r.ApprovedAtUtc.Value <= asOfUtc &&
                (!r.SupersededAtUtc.HasValue || asOfUtc < r.SupersededAtUtc.Value))
            : provenanceEligible.Where(r => r.PublishedAtUtc <= asOfUtc);

        return eligible
            .OrderByDescending(r => valuationMode == FxValuationMode.Retrospective &&
                                     r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved &&
                                     r.IsActive)
            .ThenByDescending(r => r.EffectiveFromUtc)
            .ThenBy(r => GetAuthorityRank(r.SourceAuthority))
            .ThenByDescending(r => r.PublishedAtUtc)
            .ThenByDescending(r => r.ApprovedAtUtc ?? DateTime.MinValue)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }
}
