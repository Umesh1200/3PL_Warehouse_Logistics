namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Provenance metadata for an authoritative central bank bulletin fixing release.
/// Captures official source URL, observation dates, publication time, and cryptographic digest.
/// </summary>
public record BulletinProvenanceMetadata(
    string BulletinVersion,
    string ArtifactFileName,
    string SourceUrl,
    string CentralBankAuthority,
    string BenchmarkSeriesId,
    DateTime RetrievedAtUtc,
    DateTime PublishedAtUtc,
    string HttpETag,
    string Sha256Digest,
    int ByteCount,
    string QuotationConvention,
    IReadOnlyDictionary<DateOnly, decimal> Observations);

/// <summary>
/// Authoritative provider for historical, effective-dated currency exchange rates.
/// Guarantees that financial analytics use date-accurate exchange rates with explicit source,
/// official benchmark series identity, and verifiable cryptographic SHA-256 provenance.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public interface IFxRateProvider
{
    /// <summary>
    /// Resolves the authoritative historical exchange rate for the given currency pair on the specified effective date.
    /// </summary>
    decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        out string rateSource,
        out string rateVersion);

    /// <summary>
    /// Resolves the authoritative historical exchange rate for the given currency pair on the specified effective date,
    /// returning comprehensive provenance evidence including publication timestamp, benchmark series ID, and cryptographic integrity digest.
    /// </summary>
    decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        out string rateSource,
        out string rateVersion,
        out string benchmarkSeriesId,
        out string integrityHash,
        out DateTime publishedAtUtc);

    /// <summary>
    /// Resolves the authoritative historical exchange rate enforcing that the bulletin fixing was published prior to asOfUtc valuation time.
    /// </summary>
    decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        DateTime asOfUtc,
        out string rateSource,
        out string rateVersion,
        out string benchmarkSeriesId,
        out string integrityHash,
        out DateTime publishedAtUtc);

    /// <summary>
    /// Mathematically verifies the cryptographic SHA-256 digest of an official central bank bulletin against an independently pinned trust anchor.
    /// </summary>
    bool VerifyBulletinIntegrity(string bulletinVersion, out string computedHash);

    /// <summary>
    /// Mathematically verifies the cryptographic SHA-256 digest of supplied raw bytes or an embedded bulletin against an independently pinned trust anchor.
    /// </summary>
    bool VerifyBulletinIntegrity(string bulletinVersion, byte[]? rawBytesToVerify, out string computedHash);

    /// <summary>
    /// Retrieves the raw byte payload of an archived central bank source bulletin.
    /// </summary>
    byte[] GetRawBulletinBytes(string bulletinVersion);

    /// <summary>
    /// Retrieves the full provenance metadata for an official central bank bulletin.
    /// </summary>
    BulletinProvenanceMetadata GetBulletinMetadata(string bulletinVersion);

    /// <summary>
    /// Returns the list of all registered authoritative bulletin versions.
    /// </summary>
    IReadOnlyList<string> GetSupportedBulletinVersions();
}
