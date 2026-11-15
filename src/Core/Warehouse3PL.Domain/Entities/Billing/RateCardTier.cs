using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Volume pricing tier within a rate card line.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RateCardTier : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid RateCardLineId { get; set; }

    /// <summary>
    /// Sequential index of the tier (e.g. 1, 2, 3).
    /// </summary>
    public int TierIndex { get; set; }

    /// <summary>
    /// Minimum threshold volume quantity for this tier (inclusive).
    /// </summary>
    public decimal TierMinQuantity { get; set; }

    /// <summary>
    /// Maximum threshold volume quantity for this tier (inclusive).
    /// If null, this tier represents the open-ended upper bracket (e.g. > 1,000 units).
    /// </summary>
    public decimal? TierMaxQuantity { get; set; }

    /// <summary>
    /// Unit price applied for volume within or qualifying for this tier.
    /// </summary>
    public decimal UnitPrice { get; set; }

    // Navigation
    public RateCardLine RateCardLine { get; set; } = null!;

    public RateCardTier()
    {
        Id = Guid.NewGuid();
    }

    public static RateCardTier Create(
        Guid clientId,
        Guid rateCardLineId,
        int tierIndex,
        decimal minQuantity,
        decimal? maxQuantity,
        decimal unitPrice)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for rate card tier.");
        }

        if (rateCardLineId == Guid.Empty)
        {
            throw new ValidationException(nameof(rateCardLineId), "Rate card line ID is required.");
        }

        if (tierIndex <= 0)
        {
            throw new ValidationException(nameof(tierIndex), "Tier index must be a positive integer.");
        }

        if (minQuantity < 0)
        {
            throw new ValidationException(nameof(minQuantity), "Tier minimum quantity cannot be negative.");
        }

        if (maxQuantity.HasValue && maxQuantity.Value < minQuantity)
        {
            throw new ValidationException(nameof(maxQuantity), $"Tier maximum quantity ({maxQuantity.Value}) cannot be less than minimum quantity ({minQuantity}).");
        }

        if (unitPrice < 0)
        {
            throw new ValidationException(nameof(unitPrice), "Tier unit price cannot be negative.");
        }

        return new RateCardTier
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            RateCardLineId = rateCardLineId,
            TierIndex = tierIndex,
            TierMinQuantity = decimal.Round(minQuantity, 4, MidpointRounding.AwayFromZero),
            TierMaxQuantity = maxQuantity.HasValue ? decimal.Round(maxQuantity.Value, 4, MidpointRounding.AwayFromZero) : null,
            UnitPrice = decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero)
        };
    }
}

