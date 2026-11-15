using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Advisory read-model alert triggered when an outbound shipment exhibits no tracking scan progression
/// or exceeds its contractual transit commitment by a configured threshold duration.
/// Invariant: Advisory only; does not declare legal loss or unilaterally initiate insurance/carrier claims.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class CarrierLostShipmentAlert : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    // Tenancy
    public Guid FacilityId { get; set; }
    public Guid ClientId { get; set; }

    // Milestones
    public DateTime DispatchedAtUtc { get; set; }
    public DateTime? ExpectedDeliveryDateUtc { get; set; }
    public DateTime? LastTrackingEventAtUtc { get; set; }

    public string LastKnownStatus { get; set; } = string.Empty;
    public string LastKnownLocation { get; set; } = string.Empty;

    // Threshold Breach
    public decimal HoursWithoutScanMovement { get; set; }
    public decimal PolicyThresholdHours { get; set; }
    public string TriggerReason { get; set; } = string.Empty;
    public DateTime TriggeredAtUtc { get; set; }

    // Status
    public LostShipmentAlertStatus AlertStatus { get; set; } = LostShipmentAlertStatus.Active;
    public string SlaPolicyVersion { get; set; } = string.Empty;
    public string ResolutionNotes { get; set; } = string.Empty;

    public CarrierLostShipmentAlert()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierLostShipmentAlert Create(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        Guid facilityId,
        Guid clientId,
        DateTime dispatchedAtUtc,
        DateTime? expectedDeliveryDateUtc,
        DateTime? lastTrackingEventAtUtc,
        string lastKnownStatus,
        string lastKnownLocation,
        decimal hoursWithoutScanMovement,
        decimal policyThresholdHours,
        string triggerReason,
        string slaPolicyVersion = "Default")
    {
        if (shipmentId == Guid.Empty) throw new ValidationException(nameof(shipmentId), "Shipment ID is required.");
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new ValidationException(nameof(trackingNumber), "Tracking number is required.");
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");

        return new CarrierLostShipmentAlert
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            TrackingNumber = trackingNumber.Trim(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            DispatchedAtUtc = dispatchedAtUtc.Kind == DateTimeKind.Utc ? dispatchedAtUtc : DateTime.SpecifyKind(dispatchedAtUtc, DateTimeKind.Utc),
            ExpectedDeliveryDateUtc = expectedDeliveryDateUtc.HasValue ? (expectedDeliveryDateUtc.Value.Kind == DateTimeKind.Utc ? expectedDeliveryDateUtc.Value : DateTime.SpecifyKind(expectedDeliveryDateUtc.Value, DateTimeKind.Utc)) : null,
            LastTrackingEventAtUtc = lastTrackingEventAtUtc.HasValue ? (lastTrackingEventAtUtc.Value.Kind == DateTimeKind.Utc ? lastTrackingEventAtUtc.Value : DateTime.SpecifyKind(lastTrackingEventAtUtc.Value, DateTimeKind.Utc)) : null,
            LastKnownStatus = lastKnownStatus?.Trim() ?? string.Empty,
            LastKnownLocation = lastKnownLocation?.Trim() ?? string.Empty,
            HoursWithoutScanMovement = Math.Max(0m, hoursWithoutScanMovement),
            PolicyThresholdHours = policyThresholdHours,
            TriggerReason = string.IsNullOrWhiteSpace(triggerReason) ? "Exceeded tracking scan inactivity threshold" : triggerReason.Trim(),
            TriggeredAtUtc = DateTime.UtcNow,
            AlertStatus = LostShipmentAlertStatus.Active,
            SlaPolicyVersion = string.IsNullOrWhiteSpace(slaPolicyVersion) ? "Default" : slaPolicyVersion.Trim()
        };
    }

    public void MarkResolvedDelivered(string notes = "")
    {
        AlertStatus = LostShipmentAlertStatus.ResolvedDelivered;
        ResolutionNotes = notes;
    }

    public void MarkClaimInitiated(string claimReference)
    {
        AlertStatus = LostShipmentAlertStatus.ClaimInitiated;
        ResolutionNotes = $"Claim initiated: {claimReference}";
    }

    public void Dismiss(string reason)
    {
        AlertStatus = LostShipmentAlertStatus.Dismissed;
        ResolutionNotes = reason;
    }
}
