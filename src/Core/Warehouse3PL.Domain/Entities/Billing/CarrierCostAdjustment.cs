using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Audit record capturing a carrier invoice cost discrepancy, re-weigh adjustment, or late-arriving surcharge.
/// Preserves immutable historical margin snapshots while tracking net adjustment deltas.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class CarrierCostAdjustment : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid CarrierCostRecordId { get; set; }
    public Guid? ShipmentFreightMarginRecordId { get; set; }

    public CarrierCostAdjustmentType AdjustmentType { get; set; }
    public string CarrierInvoiceNumber { get; set; } = string.Empty;

    public decimal OriginalCarrierCost { get; set; }
    public decimal AdjustedCarrierCost { get; set; }
    public decimal CarrierCostDelta { get; set; }
    public decimal ClientFreightDelta { get; set; }
    public decimal? BilledWeightKg { get; set; }

    /// <summary>
    /// Explicit adjustment currency. If null or empty, this adjustment strictly inherits
    /// the parent carrier cost record's currency under contractual reconciliation rules.
    /// It must never silently default to USD.
    /// </summary>
    public string? Currency { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AdjustedAtUtc { get; set; }

    /// <summary>
    /// Optional linkage to an adjustment BillableActivity in the commercial ledger when adjustment is billed to client.
    /// </summary>
    public Guid? BillableActivityAdjustmentId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public CarrierCostRecord CarrierCostRecord { get; set; } = null!;
    public ShipmentFreightMarginRecord? ShipmentFreightMarginRecord { get; set; }

    public CarrierCostAdjustment()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierCostAdjustment Create(
        Guid clientId,
        Guid facilityId,
        Guid carrierCostRecordId,
        CarrierCostAdjustmentType adjustmentType,
        string carrierInvoiceNumber,
        decimal originalCarrierCost,
        decimal adjustedCarrierCost,
        decimal clientFreightDelta,
        string? currency,
        string reason,
        string idempotencyKey,
        DateTime adjustedAtUtc,
        Guid? shipmentFreightMarginRecordId = null,
        Guid? billableActivityAdjustmentId = null)
    {
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (carrierCostRecordId == Guid.Empty) throw new ValidationException(nameof(carrierCostRecordId), "Carrier cost record ID is required.");
        if (string.IsNullOrWhiteSpace(carrierInvoiceNumber)) throw new ValidationException(nameof(carrierInvoiceNumber), "Carrier invoice number is required.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException(nameof(reason), "Adjustment reason is required.");

        var delta = decimal.Round(adjustedCarrierCost - originalCarrierCost, 4, MidpointRounding.AwayFromZero);
        
        string? normalizedCurrency = null;
        if (!string.IsNullOrWhiteSpace(currency))
        {
            Iso4217Currencies.Validate(currency, nameof(currency));
            normalizedCurrency = currency.Trim().ToUpperInvariant();
        }

        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;

        return new CarrierCostAdjustment
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            CarrierCostRecordId = carrierCostRecordId,
            ShipmentFreightMarginRecordId = shipmentFreightMarginRecordId,
            AdjustmentType = adjustmentType,
            CarrierInvoiceNumber = carrierInvoiceNumber.Trim(),
            OriginalCarrierCost = decimal.Round(originalCarrierCost, 4, MidpointRounding.AwayFromZero),
            AdjustedCarrierCost = decimal.Round(adjustedCarrierCost, 4, MidpointRounding.AwayFromZero),
            CarrierCostDelta = delta,
            ClientFreightDelta = decimal.Round(clientFreightDelta, 4, MidpointRounding.AwayFromZero),
            Currency = normalizedCurrency,
            Reason = reason.Trim(),
            AdjustedAtUtc = adjustedAtUtc == default ? DateTime.UtcNow : adjustedAtUtc,
            BillableActivityAdjustmentId = billableActivityAdjustmentId,
            IdempotencyKey = normalizedKey
        };
    }

    /// <summary>
    /// Explicitly resolves the effective currency for this adjustment under contractual reconciliation rules.
    /// If an explicit currency was persisted, validates and returns it (isInherited = false).
    /// If omitted or blank, explicitly inherits the parent carrier charge's currency (isInherited = true) and validates it against ISO 4217.
    /// An adjustment with unknown or unresolvable currency fails closed and is never defaulted to USD.
    /// </summary>
    public string ResolveEffectiveCurrency(string? parentChargeCurrency, out bool isInherited)
    {
        if (!string.IsNullOrWhiteSpace(Currency))
        {
            Iso4217Currencies.Validate(Currency, nameof(Currency));
            isInherited = false;
            return Currency.Trim().ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(parentChargeCurrency))
        {
            throw new InvalidOperationException($"Cannot inherit currency for adjustment '{Id}' because parent charge has no valid currency. An adjustment with unknown currency cannot be processed or defaulted to USD.");
        }

        Iso4217Currencies.Validate(parentChargeCurrency, nameof(parentChargeCurrency));
        isInherited = true;
        return parentChargeCurrency.Trim().ToUpperInvariant();
    }
}
