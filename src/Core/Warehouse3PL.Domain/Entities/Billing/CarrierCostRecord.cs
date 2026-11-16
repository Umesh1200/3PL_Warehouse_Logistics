using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Authoritative buy-side carrier transportation cost record for an outbound shipment or package.
/// Maintains buy-side accounting independence from client billable freight pricing.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class CarrierCostRecord : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? PackageId { get; set; }

    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public string? TrackingNumber { get; set; }
    public string? CarrierInvoiceNumber { get; set; }

    // Buy-Side Carrier Cost Breakdown
    public decimal BaseTransportationCost { get; set; }
    public decimal FuelSurcharge { get; set; }
    public decimal InsuranceCost { get; set; }
    public decimal AccessorialCost { get; set; }
    public decimal TotalCarrierCost { get; set; }
    public decimal? BilledWeightKg { get; set; }

    public string Currency { get; set; } = "USD";
    public CarrierCostStatus Status { get; set; } = CarrierCostStatus.Estimated;

    public DateTime CostAssessedAtUtc { get; set; }
    public DateTime? InvoicedAtUtc { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;
    public Package? Package { get; set; }

    public ICollection<CarrierAccessorialCharge> AccessorialCharges { get; set; } = new List<CarrierAccessorialCharge>();
    public ICollection<CarrierCostAdjustment> Adjustments { get; set; } = new List<CarrierCostAdjustment>();

    public CarrierCostRecord()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierCostRecord Create(
        Guid clientId,
        Guid facilityId,
        Guid shipmentId,
        string carrierCode,
        CarrierServiceLevel carrierServiceLevel,
        decimal baseCost,
        decimal fuelSurcharge,
        decimal insuranceCost,
        string currency,
        string idempotencyKey,
        DateTime costAssessedAtUtc,
        Guid? packageId = null,
        string? trackingNumber = null,
        CarrierCostStatus status = CarrierCostStatus.Estimated,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for carrier cost record.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        if (shipmentId == Guid.Empty)
        {
            throw new ValidationException(nameof(shipmentId), "Shipment ID is required.");
        }

        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        }

        if (baseCost < 0)
        {
            throw new ValidationException(nameof(baseCost), "Base transportation cost cannot be negative.");
        }

        if (fuelSurcharge < 0)
        {
            throw new ValidationException(nameof(fuelSurcharge), "Fuel surcharge cannot be negative.");
        }

        if (insuranceCost < 0)
        {
            throw new ValidationException(nameof(insuranceCost), "Insurance cost cannot be negative.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ValidationException(nameof(currency), "Currency code must be a 3-character ISO 4217 code.");
        }

        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ValidationException(nameof(idempotencyKey), "Idempotency key is required.");
        }

        var baseRounded = decimal.Round(baseCost, 4, MidpointRounding.AwayFromZero);
        var fuelRounded = decimal.Round(fuelSurcharge, 4, MidpointRounding.AwayFromZero);
        var insRounded = decimal.Round(insuranceCost, 4, MidpointRounding.AwayFromZero);
        var total = baseRounded + fuelRounded + insRounded;

        return new CarrierCostRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentId = shipmentId,
            PackageId = packageId,
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = carrierServiceLevel,
            TrackingNumber = trackingNumber?.Trim(),
            BaseTransportationCost = baseRounded,
            FuelSurcharge = fuelRounded,
            InsuranceCost = insRounded,
            AccessorialCost = 0m,
            TotalCarrierCost = total,
            Currency = normalizedCurrency,
            Status = status,
            CostAssessedAtUtc = costAssessedAtUtc == default ? DateTime.UtcNow : costAssessedAtUtc,
            IdempotencyKey = normalizedKey,
            Notes = notes
        };
    }

    /// <summary>
    /// Attaches an accessorial surcharge to this carrier cost record and updates totals.
    /// </summary>
    public void AddAccessorialCharge(CarrierAccessorialCharge charge)
    {
        if (charge == null)
        {
            throw new ValidationException(nameof(charge), "Carrier accessorial charge cannot be null.");
        }

        if (!string.Equals(charge.Currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(charge.Currency),
                $"Accessorial currency '{charge.Currency}' does not match carrier cost currency '{Currency}'.");
        }

        AccessorialCharges.Add(charge);
        RecalculateTotals();
    }

    /// <summary>
    /// Recalculates total accessorial and total carrier cost metrics from line items.
    /// </summary>
    public void RecalculateTotals()
    {
        AccessorialCost = decimal.Round(
            AccessorialCharges.Sum(a => a.CarrierAmount),
            4,
            MidpointRounding.AwayFromZero);

        TotalCarrierCost = decimal.Round(
            BaseTransportationCost + FuelSurcharge + InsuranceCost + AccessorialCost,
            4,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Reconciles carrier cost record against the final carrier invoice.
    /// </summary>
    public void ReconcileWithInvoice(
        string invoiceNumber,
        DateTime reconciledAtUtc,
        CarrierCostStatus newStatus = CarrierCostStatus.Reconciled)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ValidationException(nameof(invoiceNumber), "Carrier invoice number is required for reconciliation.");
        }

        CarrierInvoiceNumber = invoiceNumber.Trim();
        InvoicedAtUtc = reconciledAtUtc;
        ReconciledAtUtc = reconciledAtUtc;
        Status = newStatus;
    }
}

