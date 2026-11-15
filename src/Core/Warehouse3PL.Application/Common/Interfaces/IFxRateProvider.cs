namespace Warehouse3PL.Application.Common.Interfaces;

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
    /// <param name="sourceCurrency">Original ISO 4217 currency code.</param>
    /// <param name="targetCurrency">Target ISO 4217 currency code.</param>
    /// <param name="effectiveDateUtc">Effective UTC timestamp for rate resolution.</param>
    /// <param name="rateSource">Authoritative publishing body or rate feed.</param>
    /// <param name="rateVersion">Discrete publication or version tag.</param>
    /// <returns>Exchange rate multiplier.</returns>
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
    /// Mathematically verifies the cryptographic SHA-256 digest of an official central bank bulletin against its raw canonical payload.
    /// </summary>
    /// <param name="bulletinVersion">The official bulletin version tag (e.g. "FRB-20261109", "ECB-20261102").</param>
    /// <param name="computedHash">The recomputed SHA-256 hexadecimal digest.</param>
    /// <returns>True if the computed digest matches the official published integrity hash; otherwise false.</returns>
    bool VerifyBulletinIntegrity(string bulletinVersion, out string computedHash);
}
