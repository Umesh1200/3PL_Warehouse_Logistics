using System.Diagnostics.CodeAnalysis;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.ValueObjects;

/// <summary>
/// Immutable monetary value object representing an exact monetary amount and ISO 4217 currency.
/// Enforces high-precision decimal arithmetic and strict single-currency operations.
/// DB-SPEC-3PL-2026 Phase 5.0 Commercial Domain &amp; Billing Foundation.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public static readonly string DefaultCurrency = "USD";

    public Money(decimal amount, string? currency = null)
    {
        Amount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        Currency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCurrency
            : currency.Trim().ToUpperInvariant();

        if (Currency.Length != 3)
        {
            throw new ValidationException(nameof(currency), $"Currency code '{currency}' must be a valid 3-character ISO 4217 code.");
        }
    }

    public static Money Zero(string currency = "USD") => new(0m, currency);

    public static Money FromDecimal(decimal amount, string currency = "USD") => new(amount, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor)
    {
        return new Money(Amount * factor, Currency);
    }

    public Money Divide(decimal divisor)
    {
        if (divisor == 0)
        {
            throw new ValidationException(nameof(divisor), "Cannot divide monetary amount by zero.");
        }

        return new Money(Amount / divisor, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                nameof(other.Currency),
                $"Currency mismatch: Cannot operate between '{Currency}' and '{other.Currency}'.");
        }
    }

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    public static Money operator *(Money left, decimal right) => left.Multiply(right);
    public static Money operator *(decimal left, Money right) => right.Multiply(left);
    public static Money operator /(Money left, decimal right) => left.Divide(right);

    public static bool operator ==(Money left, Money right) => left.Equals(right);
    public static bool operator !=(Money left, Money right) => !left.Equals(right);
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public bool Equals(Money other) =>
        Amount == other.Amount && string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase);

    public override bool Equals([NotNullWhen(true)] object? obj) =>
        obj is Money other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Amount, Currency?.ToUpperInvariant());

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => $"{Amount:0.00##} {Currency}";
}

