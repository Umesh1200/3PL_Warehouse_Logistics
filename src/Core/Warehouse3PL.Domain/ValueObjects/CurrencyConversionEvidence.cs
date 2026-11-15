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
    /// Historical date and time at which the exchange rate was effective.
    /// </summary>
    public DateTime EffectiveDateUtc { get; init; }

    /// <summary>
    /// Authoritative source or rate provider (e.g., "FederalReserveDaily", "ECBReferenceDaily", "ContractualTenderRate").
    /// </summary>
    public string RateSource { get; init; } = "FederalReserveDaily";

    /// <summary>
    /// Discrete rate publication version or snapshot identifier (e.g., "FRB-20261101", "ExplicitCallerSnapshot").
    /// </summary>
    public string RateVersion { get; init; } = "1.0.0";

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
        string rateSource = "FederalReserveDaily",
        string rateVersion = "1.0.0",
        string targetCurrency = "USD",
        int roundingDecimals = 4,
        MidpointRounding roundingMode = MidpointRounding.AwayFromZero)
    {
        OriginalCurrency = string.IsNullOrWhiteSpace(originalCurrency) ? "USD" : originalCurrency.Trim().ToUpperInvariant();
        OriginalAmount = originalAmount;
        ExchangeRate = exchangeRate > 0m ? exchangeRate : 1.0m;
        TargetCurrency = string.IsNullOrWhiteSpace(targetCurrency) ? "USD" : targetCurrency.Trim().ToUpperInvariant();
        RateDirection = $"FROM_{OriginalCurrency}_TO_{TargetCurrency}";
        EffectiveDateUtc = effectiveDateUtc.Kind == DateTimeKind.Utc ? effectiveDateUtc : DateTime.SpecifyKind(effectiveDateUtc, DateTimeKind.Utc);
        RateSource = string.IsNullOrWhiteSpace(rateSource) ? "FederalReserveDaily" : rateSource.Trim();
        RateVersion = string.IsNullOrWhiteSpace(rateVersion) ? "1.0.0" : rateVersion.Trim();
        RoundingDecimals = roundingDecimals;
        RoundingMode = $"MidpointRounding.{roundingMode}";
        RoundingPolicy = $"{RoundingMode}, {RoundingDecimals} Decimals";

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
