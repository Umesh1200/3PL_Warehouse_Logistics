using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Granular shipment-level read model capturing SLA evaluation evidence and compliance outcomes.
/// Links operational shipment milestones to the governing contractual SLA policy version.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class CarrierSlaComplianceRecord : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    // Tenancy
    public Guid FacilityId { get; set; }
    public Guid ClientId { get; set; }

    // Geographic Lane
    public string OriginRegion { get; set; } = string.Empty;
    public string DestinationCountry { get; set; } = string.Empty;
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public string DestinationStateOrRegion { get; set; } = string.Empty;
    public string DestinationPostalCodePrefix { get; set; } = string.Empty;

    // Milestone Timestamps
    public DateTime DispatchedAtUtc { get; set; }
    public DateTime? EstimatedDeliveryDateUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    // Delivery Attempt Metrics
    public int AttemptCount { get; set; }
    public bool IsFirstAttemptSuccess { get; set; }
    public DateTime? FirstAttemptAtUtc { get; set; }

    // Transit & Variance
    public int ContractualTransitDays { get; set; }
    public decimal? ActualTransitDays { get; set; }
    public decimal? TransitTimeVarianceDays { get; set; }

    // SLA Compliance Outcomes
    public bool? IsOnTime { get; set; }
    public bool HasDeliveryException { get; set; }
    public int QualifyingExceptionsCount { get; set; }

    // SLA Policy Versioning Audit Trail
    public Guid? SlaPolicyId { get; set; }
    public string SlaPolicyVersion { get; set; } = string.Empty;
    public SlaEvaluationStatus EvaluationStatus { get; set; } = SlaEvaluationStatus.Evaluated;
    public string? DataQualityNotes { get; set; }

    public CarrierSlaComplianceRecord()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierSlaComplianceRecord Create(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        Guid facilityId,
        Guid clientId,
        DateTime dispatchedAtUtc,
        DateTime? estimatedDeliveryDateUtc,
        int contractualTransitDays,
        string slaPolicyVersion,
        Guid? slaPolicyId = null,
        string originRegion = "",
        string destinationCountry = "",
        string destinationStateOrRegion = "",
        string destinationPostalCodePrefix = "")
    {
        if (shipmentId == Guid.Empty) throw new ValidationException(nameof(shipmentId), "Shipment ID is required.");
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new ValidationException(nameof(trackingNumber), "Tracking number is required.");
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");

        var record = new CarrierSlaComplianceRecord
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            TrackingNumber = trackingNumber.Trim(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            DispatchedAtUtc = dispatchedAtUtc.Kind == DateTimeKind.Utc ? dispatchedAtUtc : DateTime.SpecifyKind(dispatchedAtUtc, DateTimeKind.Utc),
            EstimatedDeliveryDateUtc = estimatedDeliveryDateUtc.HasValue ? (estimatedDeliveryDateUtc.Value.Kind == DateTimeKind.Utc ? estimatedDeliveryDateUtc.Value : DateTime.SpecifyKind(estimatedDeliveryDateUtc.Value, DateTimeKind.Utc)) : null,
            ContractualTransitDays = contractualTransitDays,
            SlaPolicyVersion = string.IsNullOrWhiteSpace(slaPolicyVersion) ? "Default" : slaPolicyVersion.Trim(),
            SlaPolicyId = slaPolicyId,
            OriginRegion = originRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            DestinationCountry = string.IsNullOrWhiteSpace(destinationCountry) ? string.Empty : destinationCountry.Trim().ToUpperInvariant(),
            CountryNormalizationStatus = CountryNormalizationStatus.PendingNormalization,
            DestinationStateOrRegion = destinationStateOrRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            DestinationPostalCodePrefix = destinationPostalCodePrefix?.Trim() ?? string.Empty,
            AttemptCount = 0,
            IsFirstAttemptSuccess = false,
            HasDeliveryException = false,
            QualifyingExceptionsCount = 0,
            EvaluationStatus = estimatedDeliveryDateUtc.HasValue ? SlaEvaluationStatus.Evaluated : SlaEvaluationStatus.IndeterminateDueToMissingEdd
        };

        return record;
    }

    /// <summary>
    /// Records a delivery attempt event. Preserves first-attempt status immutability.
    /// Subsequent attempts increment AttemptCount without overwriting FirstAttempt outcome.
    /// </summary>
    public void RecordDeliveryAttempt(DateTime attemptUtc, bool wasSuccessful)
    {
        AttemptCount++;
        var normalized = attemptUtc.Kind == DateTimeKind.Utc ? attemptUtc : DateTime.SpecifyKind(attemptUtc, DateTimeKind.Utc);

        if (AttemptCount == 1)
        {
            FirstAttemptAtUtc = normalized;
            IsFirstAttemptSuccess = wasSuccessful;
        }
    }

    /// <summary>
    /// Records final successful delivery and evaluates contractual on-time status against EDD.
    /// Invariant: Delivered exactly on contractual EDD timestamp is considered On-Time.
    /// </summary>
    public void RecordDelivery(DateTime deliveredAtUtc)
    {
        var normalized = deliveredAtUtc.Kind == DateTimeKind.Utc ? deliveredAtUtc : DateTime.SpecifyKind(deliveredAtUtc, DateTimeKind.Utc);
        DeliveredAtUtc = normalized;

        decimal totalHours = (decimal)(normalized - DispatchedAtUtc).TotalHours;
        ActualTransitDays = Math.Round(Math.Max(0m, totalHours / 24m), 2);
        TransitTimeVarianceDays = Math.Round(ActualTransitDays.Value - ContractualTransitDays, 2);

        if (EstimatedDeliveryDateUtc.HasValue)
        {
            // Exact EDD parity: delivered on or before EDD end of day
            IsOnTime = normalized <= EstimatedDeliveryDateUtc.Value;
            EvaluationStatus = SlaEvaluationStatus.Evaluated;
        }
        else
        {
            IsOnTime = null;
            EvaluationStatus = SlaEvaluationStatus.IndeterminateDueToMissingEdd;
            DataQualityNotes = "Missing Estimated Delivery Date (EDD)";
        }
    }

    /// <summary>
    /// Records a delivery exception event.
    /// </summary>
    public void RecordException(CanonicalDeliveryExceptionCategory category)
    {
        HasDeliveryException = true;
        QualifyingExceptionsCount++;
    }
}
