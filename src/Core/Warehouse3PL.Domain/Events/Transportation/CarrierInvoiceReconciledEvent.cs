using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event establishing authoritative billed and reconciled freight charges from carrier invoice audit.
/// Normalized to charge-line level evidence with optional shipment association.
/// Distinct from tracking scans and initial rate quotes.
/// </summary>
public record CarrierInvoiceReconciledEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid InvoiceLineId { get; init; } = Guid.NewGuid();
    public string InvoiceNumber { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public string ChargeCode { get; init; } = "BASE_FREIGHT";

    // Amounts
    public decimal BilledBaseFreightUsd { get; init; }
    public decimal BilledAccessorialsUsd { get; init; }
    public decimal FuelSurchargeUsd { get; init; }
    public decimal ResidentialSurchargeUsd { get; init; }
    public decimal LiftgateFeeUsd { get; init; }
    public decimal AddressCorrectionFeeUsd { get; init; }
    public decimal OtherAdjustmentsUsd { get; init; }

    public decimal TotalBilledAmountUsd => BilledBaseFreightUsd + BilledAccessorialsUsd + FuelSurchargeUsd +
                                          ResidentialSurchargeUsd + LiftgateFeeUsd + AddressCorrectionFeeUsd + OtherAdjustmentsUsd;

    public FreightVarianceReconciliationStatus ReconciliationStatus { get; init; } = FreightVarianceReconciliationStatus.Reconciled;
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierInvoiceEdi";
}
