using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Service assessing root-cause and operational responsibility for carrier delivery exceptions and delays.
/// Separates empirical exception categories from fault attribution.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public class DelayAttributionService : IDelayAttributionService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<DelayAttributionService> _logger;

    public DelayAttributionService(
        IApplicationDbContext dbContext,
        ILogger<DelayAttributionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public DelayAttributionResultDto AttributeException(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        CanonicalDeliveryExceptionCategory category,
        string? rawCarrierCode = null,
        string? rawDescription = null,
        bool isDispatchedAfterCutoff = false)
    {
        if (isDispatchedAfterCutoff)
        {
            return new DelayAttributionResultDto
            {
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode.ToUpperInvariant(),
                CanonicalCategory = category,
                AttributionCategory = DelayAttributionCategory.FacilityOrigin,
                Confidence = AttributionConfidence.Medium,
                AttributionReason = "Shipment tendered to carrier after daily cutoff time; carrier transit clock deferred to next operating day.",
                IsCarrierResponsible = false
            };
        }

        switch (category)
        {
            case CanonicalDeliveryExceptionCategory.WeatherDisruption:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.ExternalDisruption,
                    Confidence = AttributionConfidence.High,
                    AttributionReason = "Severe weather, blizzard, road closure, or natural disaster beyond carrier network control.",
                    IsCarrierResponsible = false
                };

            case CanonicalDeliveryExceptionCategory.AddressIssue:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.CustomerOrRecipient,
                    Confidence = AttributionConfidence.High,
                    AttributionReason = "Incorrect, missing, or incomplete consignee delivery address provided by shipper/customer.",
                    IsCarrierResponsible = false
                };

            case CanonicalDeliveryExceptionCategory.RecipientUnavailable:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.CustomerOrRecipient,
                    Confidence = AttributionConfidence.High,
                    AttributionReason = "Consignee unavailable to receive parcel, signature required but absent, or commercial business closed.",
                    IsCarrierResponsible = false
                };

            case CanonicalDeliveryExceptionCategory.DamagedInTransit:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.CarrierFault,
                    Confidence = AttributionConfidence.Medium,
                    AttributionReason = "Physical package damage sustained while under carrier custody and network transit handling.",
                    IsCarrierResponsible = true
                };

            case CanonicalDeliveryExceptionCategory.CarrierOperationalDelay:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.CarrierFault,
                    Confidence = AttributionConfidence.High,
                    AttributionReason = "Carrier hub sorting delay, vehicle mechanical failure, sorting error, or network backlog.",
                    IsCarrierResponsible = true
                };

            case CanonicalDeliveryExceptionCategory.CustomsOrRegulatoryHold:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.RegulatoryOrCustoms,
                    Confidence = AttributionConfidence.High,
                    AttributionReason = "Government border clearance, quarantine inspection, or regulatory authority hold.",
                    IsCarrierResponsible = false
                };

            case CanonicalDeliveryExceptionCategory.LostOrMissing:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.CarrierFault,
                    Confidence = AttributionConfidence.Medium,
                    AttributionReason = "Parcel unlocated in carrier network; tracking scan inactivity threshold exceeded while under carrier custody.",
                    IsCarrierResponsible = true
                };

            case CanonicalDeliveryExceptionCategory.Other:
            default:
                return new DelayAttributionResultDto
                {
                    ShipmentId = shipmentId,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode.ToUpperInvariant(),
                    CanonicalCategory = category,
                    AttributionCategory = DelayAttributionCategory.Unknown,
                    Confidence = AttributionConfidence.None,
                    AttributionReason = string.IsNullOrWhiteSpace(rawDescription)
                        ? "Generic or unclassified carrier status code; insufficient evidence to establish operational responsibility."
                        : $"Unclassified carrier event: {rawDescription.Trim()}",
                    IsCarrierResponsible = false
                };
        }
    }

    /// <summary>
    /// Evaluates operational delay attribution specifically for lost shipment alerts.
    /// Invariant: A lost alert by itself does not establish carrier liability.
    /// Preserves Unknown when carrier custody transfer is unproven or policy threshold is not breached.
    /// </summary>
    public DelayAttributionResultDto AttributeLostShipment(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        bool hasCarrierScanEvidence,
        bool isThresholdBreached,
        string? details = null)
    {
        // 1. If carrier never scanned parcel in network, custody handoff is unproven -> Unknown
        if (!hasCarrierScanEvidence)
        {
            return new DelayAttributionResultDto
            {
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode.ToUpperInvariant(),
                CanonicalCategory = CanonicalDeliveryExceptionCategory.LostOrMissing,
                AttributionCategory = DelayAttributionCategory.Unknown,
                Confidence = AttributionConfidence.None,
                AttributionReason = "No carrier network custody scans recorded. Custody handoff is unverified; cannot establish carrier responsibility.",
                IsCarrierResponsible = false
            };
        }

        // 2. If carrier scanned parcel in custody and policy threshold is breached -> CarrierFault (Medium confidence)
        if (isThresholdBreached)
        {
            return new DelayAttributionResultDto
            {
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode.ToUpperInvariant(),
                CanonicalCategory = CanonicalDeliveryExceptionCategory.LostOrMissing,
                AttributionCategory = DelayAttributionCategory.CarrierFault,
                Confidence = AttributionConfidence.Medium,
                AttributionReason = string.IsNullOrWhiteSpace(details)
                    ? "Parcel scan inactivity exceeds contractual threshold while under verified carrier network custody."
                    : details,
                IsCarrierResponsible = true
            };
        }

        // 3. Carrier has parcel in network, but inactivity has not yet breached threshold -> Unknown
        return new DelayAttributionResultDto
        {
            ShipmentId = shipmentId,
            TrackingNumber = trackingNumber,
            CarrierCode = carrierCode.ToUpperInvariant(),
            CanonicalCategory = CanonicalDeliveryExceptionCategory.LostOrMissing,
            AttributionCategory = DelayAttributionCategory.Unknown,
            Confidence = AttributionConfidence.Low,
            AttributionReason = "Inactivity observed in carrier network but contractual lost shipment threshold hours not yet breached.",
            IsCarrierResponsible = false
        };
    }

    public async Task<IReadOnlyList<DelayAttributionResultDto>> GetShipmentDelayAttributionsAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var exceptions = await _dbContext.ShipmentDeliveryExceptions
            .AsNoTracking()
            .Where(e => e.ShipmentId == shipmentId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        if (exceptions.Count == 0)
        {
            return Array.Empty<DelayAttributionResultDto>();
        }

        var results = new List<DelayAttributionResultDto>(exceptions.Count);
        foreach (var ex in exceptions)
        {
            // Map legacy / operational exception to canonical category
            var canonical = MapToCanonicalCategory(ex.ExceptionType.ToString(), ex.CarrierDescription);
            var result = AttributeException(
                shipmentId,
                ex.TrackingNumber,
                "CARRIER",
                canonical,
                ex.CarrierExceptionCode,
                ex.CarrierDescription);

            results.Add(result);
        }

        return results;
    }

    private static CanonicalDeliveryExceptionCategory MapToCanonicalCategory(string exceptionType, string reason)
    {
        var combined = $"{exceptionType} {reason}".ToUpperInvariant();

        if (combined.Contains("WEATHER") || combined.Contains("SNOW") || combined.Contains("STORM") || combined.Contains("FLOOD"))
            return CanonicalDeliveryExceptionCategory.WeatherDisruption;
        if (combined.Contains("ADDRESS") || combined.Contains("STREET") || combined.Contains("APT") || combined.Contains("SUITE"))
            return CanonicalDeliveryExceptionCategory.AddressIssue;
        if (combined.Contains("CLOSED") || combined.Contains("UNAVAILABLE") || combined.Contains("NO ONE") || combined.Contains("NOT HOME"))
            return CanonicalDeliveryExceptionCategory.RecipientUnavailable;
        if (combined.Contains("DAMAGE") || combined.Contains("BROKEN") || combined.Contains("LEAK"))
            return CanonicalDeliveryExceptionCategory.DamagedInTransit;
        if (combined.Contains("MECHANICAL") || combined.Contains("SORT") || combined.Contains("DELAY") || combined.Contains("BACKLOG"))
            return CanonicalDeliveryExceptionCategory.CarrierOperationalDelay;
        if (combined.Contains("CUSTOMS") || combined.Contains("DUTY") || combined.Contains("BORDER") || combined.Contains("REGULATORY"))
            return CanonicalDeliveryExceptionCategory.CustomsOrRegulatoryHold;
        if (combined.Contains("LOST") || combined.Contains("MISSING") || combined.Contains("NO SCAN"))
            return CanonicalDeliveryExceptionCategory.LostOrMissing;

        return CanonicalDeliveryExceptionCategory.Other;
    }
}
