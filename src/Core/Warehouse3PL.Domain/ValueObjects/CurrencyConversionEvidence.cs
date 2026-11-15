namespace Warehouse3PL.Domain.ValueObjects;

/// <summary>
/// Immutable audit record capturing reproducible currency conversion evidence for financial analytics.
/// Guarantees that historical financial records remain deterministic, traceable, and frozen
/// regardless of subsequent market exchange rate movements.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public record CurrencyConversionEvidence
{
    /// <summary>
    /// ISO 4217 original currency code prior to normalization (e.g., "CAD", "EUR", "USD").
    /// </summary>
    public string OriginalCurrency { get; init; } = "USD";

    /// <summary>
    /// Original financial amount in the source currency.
    /// </summary>
    public decimal OriginalAmount { get; init; }

    /// <summary>
    /// Exchange rate applied during conversion (multiplier: ConvertedAmount = OriginalAmount * ExchangeRate).
    /// </summary>
    public decimal ExchangeRate { get; init; } = 1.0m;

    /// <summary>
    /// Unambiguous direction of the applied rate: e.g., "FROM_CAD_TO_USD".
    /// </summary>
    public string RateDirection { get; init; } = "FROM_USD_TO_USD";

    /// <summary>
    /// ISO 4217 target currency code (standardized to "USD").
    /// </summary>
    public string TargetCurrency { get; init; } = "USD";

    /// <summary>
    /// Normalized amount converted to target currency using explicit rounding policy.
    /// </summary>
    public decimal ConvertedAmountUsd { get; init; }

    /// <summary>
    /// Historical date and time at which the exchange rate was effective in UTC.
    /// </summary>
    public DateTime EffectiveDateUtc { get; init; }

    /// <summary>
    /// Authoritative source or rate provider (e.g., "FederalReserveBoard_H10", "EuropeanCentralBank_Reference", "BankOfEngland_SpotStatistical").
    /// </summary>
    public string RateSource { get; init; } = "FederalReserveBoard_H10";

    /// <summary>
    /// Discrete rate publication version or snapshot identifier (e.g., "FRB-20261102", "ExplicitCallerSnapshot").
    /// </summary>
    public string RateVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Central bank benchmark series identifier (e.g., "FRB_H10_G10_CAD", "ECB_EXR_D_EUR_USD_SP00_A").
    /// </summary>
    public string? BenchmarkSeriesId { get; init; }

    /// <summary>
    /// SHA-256 cryptographic digest verifying the immutable publication bulletin payload.
    /// </summary>
    public string? IntegrityHash { get; init; }

    /// <summary>
    /// Exact UTC timestamp when the fixing bulletin was released by the monetary authority.
    /// </summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>
    /// Precision decimal places applied to the converted target amount (standard: 4 decimals).
    /// </summary>
    public int RoundingDecimals { get; init; } = 4;

    /// <summary>
    /// Explicit rounding mode applied (standard: MidpointRounding.AwayFromZero).
    /// </summary>
    public string RoundingMode { get; init; } = "MidpointRounding.AwayFromZero";

    /// <summary>
    /// Comprehensive audit string summarizing precision and rounding policy.
    /// </summary>
    public string RoundingPolicy { get; init; } = "MidpointRounding.AwayFromZero, 4 Decimals";

    public CurrencyConversionEvidence()
    {
    }

    public CurrencyConversionEvidence(
        string originalCurrency,
        decimal originalAmount,
        decimal exchangeRate,
        DateTime effectiveDateUtc,
        string rateSource = "FederalReserveBoard_H10",
        string rateVersion = "1.0.0",
        string targetCurrency = "USD",
        int roundingDecimals = 4,
        MidpointRounding roundingMode = MidpointRounding.AwayFromZero,
        string? benchmarkSeriesId = null,
        string? integrityHash = null,
        DateTime? publishedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(originalCurrency))
        {
            throw new ArgumentException("Original currency code cannot be null or empty.", nameof(originalCurrency));
        }

        if (string.IsNullOrWhiteSpace(targetCurrency))
        {
            throw new ArgumentException("Target currency code cannot be null or empty.", nameof(targetCurrency));
        }

        if (exchangeRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(exchangeRate), exchangeRate, "Exchange rate must be strictly positive (> 0).");
        }

        if (effectiveDateUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("Effective date cannot have DateTimeKind.Unspecified. Must be explicitly Utc or Local.", nameof(effectiveDateUtc));
        }

        OriginalCurrency = originalCurrency.Trim().ToUpperInvariant();
        OriginalAmount = originalAmount;
        ExchangeRate = exchangeRate;
        TargetCurrency = targetCurrency.Trim().ToUpperInvariant();
        RateDirection = $"FROM_{OriginalCurrency}_TO_{TargetCurrency}";
        EffectiveDateUtc = effectiveDateUtc.Kind == DateTimeKind.Local ? effectiveDateUtc.ToUniversalTime() : effectiveDateUtc;
        RateSource = string.IsNullOrWhiteSpace(rateSource) ? "FederalReserveBoard_H10" : rateSource.Trim();
        RateVersion = string.IsNullOrWhiteSpace(rateVersion) ? "1.0.0" : rateVersion.Trim();
        RoundingDecimals = roundingDecimals;
        RoundingMode = $"MidpointRounding.{roundingMode}";
        RoundingPolicy = $"{RoundingMode}, {RoundingDecimals} Decimals";
        BenchmarkSeriesId = benchmarkSeriesId;
        IntegrityHash = integrityHash;
        PublishedAtUtc = publishedAtUtc;

        if (string.Equals(OriginalCurrency, TargetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            ExchangeRate = 1.0m;
            ConvertedAmountUsd = originalAmount;
        }
        else
        {
            ConvertedAmountUsd = Math.Round(originalAmount * ExchangeRate, roundingDecimals, roundingMode);
        }
    }
}
