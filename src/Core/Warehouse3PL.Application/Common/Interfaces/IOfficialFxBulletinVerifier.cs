using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public sealed record OfficialBulletinVerificationResult(
    string BulletinVersion,
    FxRateSourceAuthority Authority,
    string BenchmarkSeriesId,
    string VerifiedIntegrityHash,
    string EvidenceReference,
    DateTime PublishedAtUtc,
    string TransformationTrace);

/// <summary>
/// Independently verifies official central-bank FX bulletins (NRB, FRB, ECB, BOE) against retained/pinned
/// trust-anchor artifacts, cryptographically validating SHA-256 digests and parsed quotation tuples
/// (currency pair, quotation direction, unit multiplier, buy/sell/mid rates, publication timestamp).
/// Manual governance rates (<see cref="FxRateSourceAuthority.ManualApproved"/>) are verified by explicit Staff
/// governance rather than external central-bank fixtures.
/// </summary>
public interface IOfficialFxBulletinVerifier
{
    OfficialBulletinVerificationResult VerifyOfficialQuotation(
        FxRateSourceAuthority authority,
        string bulletinVersion,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        FxRateDirection rateDirection,
        int unitMultiplier,
        decimal buyRate,
        decimal sellRate,
        decimal midRate,
        DateTime publishedAtUtc,
        string? suppliedBenchmarkSeriesId = null,
        string? suppliedIntegrityHash = null,
        string? suppliedArtifactPayload = null);
}
