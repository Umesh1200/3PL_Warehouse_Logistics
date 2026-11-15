namespace Warehouse3PL.Domain.ValueObjects;

/// <summary>
/// Authoritative registry and validator for ISO 4217 three-letter currency codes.
/// Maintains the active standard currency codes established by the International Organization for Standardization (ISO).
/// Enforces strict uppercase membership validation, rejecting unassigned, obsolete, or syntactically invalid strings.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public static class Iso4217Currencies
{
    /// <summary>
    /// Active official ISO 4217 3-letter currency codes.
    /// Source: SIX Financial Information / ISO 4217 Maintenance Agency.
    /// </summary>
    private static readonly HashSet<string> ActiveIso4217Codes = new(StringComparer.Ordinal)
    {
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BOV",
        "BRL", "BSD", "BTN", "BWP", "BYN", "BZD", "CAD", "CDF", "CHE", "CHF",
        "CHW", "CLF", "CLP", "CNY", "COP", "COU", "CRC", "CUC", "CUP", "CVE",
        "CZK", "DJF", "DKK", "DOP", "DZD", "EGP", "ERN", "ETB", "EUR", "FJD",
        "FKP", "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD", "HKD",
        "HNL", "HRK", "HTG", "HUF", "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY", "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD",
        "KYD", "KZT", "LAK", "LBP", "LKR", "LRD", "LSL", "LYD", "MAD", "MDL",
        "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN",
        "MXV", "MYR", "MZN", "NAD", "NGN", "NIO", "NOK", "NPR", "NZD", "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG", "QAR", "RON", "RSD",
        "RUB", "RWF", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLL",
        "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS",
        "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "UGX", "USD",
        "USN", "UYI", "UYU", "UYW", "UZS", "VED", "VES", "VND", "VUV", "WST",
        "XAF", "XCD", "XDR", "XOF", "XPF", "XSU", "XUA", "YER", "ZAR", "ZMW",
        "ZWL"
    };

    /// <summary>
    /// Checks whether the supplied string is an active uppercase ISO 4217 currency code.
    /// </summary>
    public static bool IsValid(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return false;
        }

        var trimmed = currencyCode.Trim();
        return trimmed.Length == 3 && ActiveIso4217Codes.Contains(trimmed);
    }

    /// <summary>
    /// Validates that the supplied string conforms to an active, assigned ISO 4217 3-letter currency code.
    /// Throws ArgumentException if null, whitespace, incorrect length, lowercase, or unassigned.
    /// </summary>
    public static void Validate(string? currencyCode, string paramName)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code cannot be null or whitespace.", paramName);
        }

        var trimmed = currencyCode.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                $"Currency code '{currencyCode}' must consist of exactly 3 uppercase ASCII letters.", paramName);
        }

        if (!ActiveIso4217Codes.Contains(trimmed))
        {
            throw new ArgumentException(
                $"Currency code '{currencyCode}' is not an active standard ISO 4217 currency code.", paramName);
        }
    }
}
