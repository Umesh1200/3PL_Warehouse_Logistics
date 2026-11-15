using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Discrete buy-side carrier accessorial surcharge line item assessed against a shipment or package.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class CarrierAccessorialCharge : AuditableEntity<Guid>, ITenantEntity
{
    public Guid CarrierCostRecordId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? PackageId { get; set; }

    public AccessorialType AccessorialType { get; set; }
    public string CarrierAccessorialCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public decimal CarrierAmount { get; set; }
    public decimal? ClientAmount { get; set; }
    public AccessorialBillableTreatment BillableTreatment { get; set; } = AccessorialBillableTreatment.BillableWithMarkup;

    public string Currency { get; set; } = "USD";
    public DateTime AssessedAtUtc { get; set; }
    public string? SourceDocumentReference { get; set; }

    public bool IsReconciled { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    // Navigations
    public CarrierCostRecord CarrierCostRecord { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;
    public Package? Package { get; set; }

    public CarrierAccessorialCharge()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierAccessorialCharge Create(
        Guid carrierCostRecordId,
        Guid clientId,
        Guid facilityId,
        Guid shipmentId,
        AccessorialType accessorialType,
        string carrierAccessorialCode,
        decimal carrierAmount,
        string currency,
        string idempotencyKey,
        DateTime assessedAtUtc,
        Guid? packageId = null,
        string? description = null,
        string? sourceDocumentReference = null,
        AccessorialBillableTreatment billableTreatment = AccessorialBillableTreatment.BillableWithMarkup,
        decimal? clientAmount = null)
    {
        if (carrierCostRecordId == Guid.Empty)
        {
            throw new ValidationException(nameof(carrierCostRecordId), "Carrier cost record ID is required.");
        }

        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        if (shipmentId == Guid.Empty)
        {
            throw new ValidationException(nameof(shipmentId), "Shipment ID is required.");
        }

        if (string.IsNullOrWhiteSpace(carrierAccessorialCode))
        {
            throw new ValidationException(nameof(carrierAccessorialCode), "Carrier accessorial code is required.");
        }

        if (carrierAmount < 0)
        {
            throw new ValidationException(nameof(carrierAmount), "Carrier accessorial amount cannot be negative.");
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

        return new CarrierAccessorialCharge
        {
            Id = Guid.NewGuid(),
            CarrierCostRecordId = carrierCostRecordId,
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentId = shipmentId,
            PackageId = packageId,
            AccessorialType = accessorialType,
            CarrierAccessorialCode = carrierAccessorialCode.Trim().ToUpperInvariant(),
            Description = string.IsNullOrWhiteSpace(description) ? $"{accessorialType} surcharge" : description.Trim(),
            CarrierAmount = decimal.Round(carrierAmount, 4, MidpointRounding.AwayFromZero),
            ClientAmount = clientAmount.HasValue ? decimal.Round(clientAmount.Value, 4, MidpointRounding.AwayFromZero) : null,
            BillableTreatment = billableTreatment,
            Currency = normalizedCurrency,
            AssessedAtUtc = assessedAtUtc == default ? DateTime.UtcNow : assessedAtUtc,
            SourceDocumentReference = sourceDocumentReference?.Trim(),
            IsReconciled = false,
            IdempotencyKey = normalizedKey
        };
    }

    /// <summary>
    /// Rates this accessorial for client billing based on the resolved client commercial policy.
    /// </summary>
    public void RateClientCharge(decimal clientCharge, AccessorialBillableTreatment treatment)
    {
        if (clientCharge < 0)
        {
            throw new ValidationException(nameof(clientCharge), "Client accessorial charge cannot be negative.");
        }

        ClientAmount = decimal.Round(clientCharge, 4, MidpointRounding.AwayFromZero);
        BillableTreatment = treatment;
    }
}

