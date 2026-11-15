using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class IngestCarrierCostRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? PackageId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public string? TrackingNumber { get; set; }
    public decimal BaseTransportationCost { get; set; }
    public decimal FuelSurcharge { get; set; }
    public decimal InsuranceCost { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime CostAssessedAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public CarrierCostStatus Status { get; set; } = CarrierCostStatus.Estimated;
    public string? Notes { get; set; }
    public List<IngestAccessorialChargeDto>? Accessorials { get; set; }
}

public class IngestAccessorialChargeDto
{
    public AccessorialType AccessorialType { get; set; }
    public string CarrierAccessorialCode { get; set; } = string.Empty;
    public decimal CarrierAmount { get; set; }
    public string? Description { get; set; }
    public string? SourceDocumentReference { get; set; }
    public Guid? PackageId { get; set; }
}

public class IngestCarrierCostResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid CarrierCostRecordId { get; set; }
    public Guid ShipmentId { get; set; }
    public decimal TotalCarrierCost { get; set; }
    public string Currency { get; set; } = "USD";
    public CarrierCostStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CreateFreightAgreementRequest
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public string AgreementCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CarrierCode { get; set; }
    public CarrierServiceLevel? CarrierServiceLevel { get; set; }
    public FreightPricingModel PricingModel { get; set; } = FreightPricingModel.CostPlusPercentageMarkup;
    public decimal? MarkupPercentage { get; set; }
    public decimal? FlatFeePerShipment { get; set; }
    public decimal? FlatFeePerPackage { get; set; }
    public decimal? FixedFreightRate { get; set; }
    public decimal? MinimumMarkupAmount { get; set; }
    public decimal? MaximumMarkupAmount { get; set; }
    public AccessorialBillableTreatment DefaultAccessorialTreatment { get; set; } = AccessorialBillableTreatment.BillableWithMarkup;
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string? Notes { get; set; }
}

public class RateShipmentFreightResult
{
    public bool IsSuccess { get; set; }
    public bool IsAlreadyRated { get; set; }
    public Guid MarginRecordId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? BillableActivityId { get; set; }
    public decimal TotalCarrierCost { get; set; }
    public decimal TotalClientFreight { get; set; }
    public decimal GrossMarginAmount { get; set; }
    public decimal MarkupPercentage { get; set; }
    public decimal MarginPercentage { get; set; }
    public string Currency { get; set; } = "USD";
    public FreightPricingModel PricingModelApplied { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ReconcileCarrierInvoiceRequest
{
    public Guid ClientId { get; set; }
    public Guid ShipmentId { get; set; }
    public string CarrierInvoiceNumber { get; set; } = string.Empty;
    public decimal InvoicedTotalCost { get; set; }
    public CarrierCostAdjustmentType AdjustmentType { get; set; } = CarrierCostAdjustmentType.RateCorrection;
    public string Reason { get; set; } = string.Empty;
    public DateTime ReconciledAtUtc { get; set; }
    public bool BillAdjustmentToClient { get; set; } = true;
    public decimal? ClientFreightDeltaOverride { get; set; }
}

public class ReconcileCarrierInvoiceResult
{
    public bool IsSuccess { get; set; }
    public bool HasDiscrepancy { get; set; }
    public Guid CarrierCostRecordId { get; set; }
    public Guid? AdjustmentId { get; set; }
    public Guid? AdjustmentBillableActivityId { get; set; }
    public decimal OriginalCarrierCost { get; set; }
    public decimal InvoicedCarrierCost { get; set; }
    public decimal CarrierCostDelta { get; set; }
    public decimal ClientFreightDelta { get; set; }
    public decimal NewNetGrossMargin { get; set; }
    public string Currency { get; set; } = "USD";
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Authoritative service interface for Shipping Margin, Accessorial Charges, and Carrier Markup calculations.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public interface IFreightMarginEngineService
{
    /// <summary>
    /// Ingests a buy-side carrier transportation cost record for a shipment with deterministic idempotency.
    /// </summary>
    Task<IngestCarrierCostResult> IngestCarrierCostAsync(
        IngestCarrierCostRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a discrete carrier accessorial surcharge against an existing carrier cost record.
    /// </summary>
    Task<CarrierAccessorialCharge> IngestAccessorialChargeAsync(
        Guid carrierCostRecordId,
        AccessorialType accessorialType,
        string carrierAccessorialCode,
        decimal carrierAmount,
        string currency,
        string idempotencyKey,
        DateTime assessedAtUtc,
        Guid? packageId = null,
        string? description = null,
        string? sourceDocumentReference = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commercial freight markup agreement for a client tenant.
    /// </summary>
    Task<ClientFreightAgreement> CreateFreightAgreementAsync(
        CreateFreightAgreementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates an accessorial override rule under a client freight agreement.
    /// </summary>
    Task<ClientAccessorialRule> AddAccessorialRuleAsync(
        Guid agreementId,
        AccessorialType accessorialType,
        AccessorialBillableTreatment treatment,
        decimal? markupPercentage = null,
        decimal? fixedFee = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the effective freight markup agreement for a client, facility, carrier, and service level.
    /// Precedence: Facility exact carrier/service -> Facility carrier -> Facility default -> Client carrier/service -> Client carrier -> Client default.
    /// </summary>
    Task<ClientFreightAgreement?> GetEffectiveFreightAgreementAsync(
        Guid clientId,
        Guid? facilityId,
        string? carrierCode,
        CarrierServiceLevel? serviceLevel,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates contractual shipping markup and accessorial charges for a shipment, calculates buy vs sell margin,
    /// preserves historical snapshot, and emits a BillableActivity ledger record.
    /// </summary>
    Task<RateShipmentFreightResult> RateShipmentFreightAsync(
        Guid shipmentId,
        bool autoCreateBillableActivity = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles carrier cost record against the final carrier billing invoice, recording discrepancies
    /// as adjustments, updating net adjusted margin, and optionally billing adjustments to the client.
    /// </summary>
    Task<ReconcileCarrierInvoiceResult> ReconcileCarrierInvoiceCostAsync(
        ReconcileCarrierInvoiceRequest request,
        CancellationToken cancellationToken = default);
}

