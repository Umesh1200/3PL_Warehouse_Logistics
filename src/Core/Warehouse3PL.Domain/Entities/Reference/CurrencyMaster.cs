using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Authoritative ISO 4217 currency reference record owning numeric code and minor-unit rounding precision.
/// Platform-seeded immutable catalog; tenants cannot create or mutate currency definitions.
/// </summary>
public class CurrencyMaster
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string NumericCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int MinorUnits { get; set; } = 2;
    public bool SymbolBeforeAmount { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SeedVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public static CurrencyMaster Create(
        string currencyCode,
        string numericCode,
        string name,
        string symbol,
        int minorUnits,
        bool symbolBeforeAmount = true,
        bool isActive = true,
        int seedVersion = 1)
    {
        try
        {
            Iso4217Currencies.Validate(currencyCode, nameof(currencyCode));
        }
        catch (ArgumentException ex)
        {
            throw new DomainException(ex.Message, "CURRENCY_INVALID_ISO4217");
        }

        var normalizedCode = currencyCode.Trim().ToUpperInvariant();
        var normalizedNumeric = (numericCode ?? string.Empty).Trim();
        if (normalizedNumeric.Length != 3 || !normalizedNumeric.All(char.IsAsciiDigit))
        {
            throw new DomainException("ISO 4217 NumericCode must be exactly 3 ASCII digits.", "CURRENCY_INVALID_NUMERIC_CODE");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new DomainException("Currency Name is required and must not exceed 120 characters.", "CURRENCY_INVALID_NAME");
        }

        if (string.IsNullOrWhiteSpace(symbol) || symbol.Trim().Length > 16)
        {
            throw new DomainException("Currency Symbol is required and must not exceed 16 characters.", "CURRENCY_INVALID_SYMBOL");
        }

        if (minorUnits < 0 || minorUnits > 4)
        {
            throw new DomainException("MinorUnits must be between 0 and 4 decimal places.", "CURRENCY_INVALID_MINOR_UNITS");
        }

        return new CurrencyMaster
        {
            CurrencyCode = normalizedCode,
            NumericCode = normalizedNumeric,
            Name = name.Trim(),
            Symbol = symbol.Trim(),
            MinorUnits = minorUnits,
            SymbolBeforeAmount = symbolBeforeAmount,
            IsActive = isActive,
            SeedVersion = seedVersion,
            CreatedAt = DateTime.UtcNow
        };
    }

    public decimal RoundAmount(decimal amount)
        => Math.Round(amount, MinorUnits, MidpointRounding.AwayFromZero);
}
