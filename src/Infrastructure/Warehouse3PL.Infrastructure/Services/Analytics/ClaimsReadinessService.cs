using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Service assessing evidentiary completeness for carrier loss, damage, or SLA breach claims.
/// Operates as an objective evidence audit; does not initiate legal loss or automated billing debits.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public class ClaimsReadinessService : IClaimsReadinessService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ClaimsReadinessService> _logger;

    public ClaimsReadinessService(
        IApplicationDbContext dbContext,
        ILogger<ClaimsReadinessService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ClaimsReadinessAssessmentDto> AssessShipmentClaimsReadinessAsync(
        Guid shipmentId,
        ClaimType claimType = ClaimType.Lost,
        CancellationToken cancellationToken = default)
    {
        var compliance = await _dbContext.CarrierSlaComplianceRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ShipmentId == shipmentId, cancellationToken);

        var alert = await _dbContext.CarrierLostShipmentAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ShipmentId == shipmentId, cancellationToken);

        var trackingScansCount = await _dbContext.ShipmentTrackingEvents
            .AsNoTracking()
            .CountAsync(t => t.ShipmentId == shipmentId, cancellationToken);

        var exceptionsCount = await _dbContext.ShipmentDeliveryExceptions
            .AsNoTracking()
            .CountAsync(e => e.ShipmentId == shipmentId, cancellationToken);

        var costRecord = await _dbContext.CarrierCostRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ShipmentId == shipmentId, cancellationToken);

        var pod = await _dbContext.ProofOfDeliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ShipmentId == shipmentId, cancellationToken);

        var carrierCode = compliance?.CarrierCode ?? alert?.CarrierCode ?? costRecord?.CarrierCode ?? "UNKNOWN";
        var trackingNumber = compliance?.TrackingNumber ?? alert?.TrackingNumber ?? costRecord?.TrackingNumber ?? "UNKNOWN";
        var facilityId = compliance?.FacilityId ?? alert?.FacilityId ?? costRecord?.FacilityId ?? Guid.Empty;
        var clientId = compliance?.ClientId ?? alert?.ClientId ?? costRecord?.ClientId ?? Guid.Empty;

        // Evidence Dimension Checklist
        bool hasHandover = compliance != null && compliance.DispatchedAtUtc != default;
        bool hasTracking = trackingScansCount > 0;
        bool hasDisruption = exceptionsCount > 0 || alert != null;
        bool hasThresholdBreach = alert != null && alert.HoursWithoutScanMovement >= alert.PolicyThresholdHours;
        bool hasFreightCost = costRecord != null && costRecord.TotalCarrierCost > 0m;
        bool hasValuation = costRecord != null && (costRecord.TotalCarrierCost > 0m || costRecord.InsuranceCost > 0m);
        bool hasPod = pod != null;

        return EvaluateReadinessCriteria(
            shipmentId,
            trackingNumber,
            carrierCode,
            facilityId,
            clientId,
            hasHandover,
            hasTracking,
            hasDisruption,
            hasThresholdBreach,
            hasFreightCost,
            hasValuation,
            hasPod,
            explicitScorePercent: null,
            claimType: claimType);
    }

    /// <summary>
    /// Evaluates claims readiness from discrete evidentiary criteria and enforces mandatory evidence gates.
    /// Invariant: High percentage completeness alone never overrides a missing mandatory evidence item.
    /// </summary>
    public ClaimsReadinessAssessmentDto EvaluateReadinessCriteria(
        Guid shipmentId,
        string trackingNumber,
        string carrierCode,
        Guid facilityId,
        Guid clientId,
        bool hasHandover,
        bool hasTracking,
        bool hasDisruption,
        bool hasThresholdBreach,
        bool hasFreightCost,
        bool hasValuation,
        bool hasPod,
        decimal? explicitScorePercent = null,
        ClaimType claimType = ClaimType.Lost)
    {
        var missingItems = new List<string>();
        if (!hasHandover) missingItems.Add("Physical warehouse handover confirmation timestamp missing.");
        if (!hasTracking) missingItems.Add("Carrier origin or intermediate transit scan evidence missing.");
        if (!hasDisruption) missingItems.Add("Documented delivery exception or tracking inactivity alert missing.");
        if (!hasFreightCost) missingItems.Add("Authoritative carrier invoice or buy-side freight cost record missing.");
        if (!hasValuation) missingItems.Add("Declared goods replacement valuation missing.");

        // Conditional item requirements
        if (claimType == ClaimType.Lost)
        {
            if (!hasThresholdBreach)
            {
                missingItems.Add("Carrier tracking inactivity policy threshold hours have not been breached.");
            }
            if (hasPod)
            {
                missingItems.Add("Verified Proof of Delivery scan exists, contradicting lost parcel claim.");
            }
        }
        else if (claimType == ClaimType.Damaged)
        {
            if (!hasPod && !hasDisruption)
            {
                missingItems.Add("Delivery Proof of Delivery record with damage notation or physical exception scan missing.");
            }
        }

        // Standardized Contractual Weights:
        // Mandatory: Handover (20%), Tracking (15%), Disruption (20%), FreightCost (15%), Valuation (15%) = 85%
        // Conditional: ThresholdBreach (10%), POD (5%) = 15%
        // Total Potential = 100%
        decimal scorePercent;
        if (explicitScorePercent.HasValue)
        {
            scorePercent = explicitScorePercent.Value;
        }
        else
        {
            decimal score = 0m;
            if (hasHandover) score += 20m;
            if (hasTracking) score += 15m;
            if (hasDisruption) score += 20m;
            if (hasFreightCost) score += 15m;
            if (hasValuation) score += 15m;
            if (hasThresholdBreach) score += 10m;
            if (hasPod) score += 5m;
            scorePercent = score;
        }

        bool allMandatoryPresent = hasHandover && hasTracking && hasDisruption && hasFreightCost && hasValuation;

        ClaimsReadinessStatus status;
        string reason;

        // Canonical Readiness Policy:
        // 1. Mandatory Gate & Insufficient Evidence (< 50% or critical mandatory item missing)
        if (scorePercent < 50.0m || !allMandatoryPresent)
        {
            status = ClaimsReadinessStatus.InsufficientEvidence;
            reason = !allMandatoryPresent
                ? $"Mandatory evidence gate breach: claim review blocked due to missing mandatory documentation ({string.Join("; ", missingItems.Where(m => !m.Contains("threshold") && !m.Contains("contradicting")))})."
                : $"Insufficient evidence ({scorePercent}% completeness, minimum 50% required for collecting state). Critical documentation missing.";
        }
        // 2. Evidence Collecting (50% <= score < 85%, or mandatory present but conditional items outstanding)
        else if (scorePercent < 85.0m)
        {
            status = ClaimsReadinessStatus.EvidenceCollecting;
            reason = $"Partial evidence compiled ({scorePercent}% completeness). Awaiting outstanding documentation before claims adjustor review.";
        }
        // 3. Ready For Review (>= 85%, all mandatory items present, and required conditional disruption/loss established)
        else
        {
            if (claimType == ClaimType.Lost)
            {
                if (!hasThresholdBreach)
                {
                    status = ClaimsReadinessStatus.EvidenceCollecting;
                    reason = "All mandatory documentation present, but tracking inactivity duration has not yet exceeded contractual threshold hours for a lost parcel claim.";
                }
                else if (hasPod)
                {
                    status = ClaimsReadinessStatus.InsufficientEvidence;
                    reason = "Claim review blocked: clean Proof of Delivery exists, contradicting lost shipment claim.";
                }
                else
                {
                    status = ClaimsReadinessStatus.ReadyForReview;
                    reason = "Complete evidentiary package assembled: physical handover, carrier tracking scans, threshold breach, freight invoice, and replacement valuation verified.";
                }
            }
            else if (claimType == ClaimType.Damaged)
            {
                if (!hasPod && !hasDisruption)
                {
                    status = ClaimsReadinessStatus.EvidenceCollecting;
                    reason = "All mandatory documentation present, but POD inspection record or physical damage exception is missing.";
                }
                else
                {
                    status = ClaimsReadinessStatus.ReadyForReview;
                    reason = "Complete evidentiary package assembled: physical handover, carrier tracking, physical transit damage evidence, freight invoice, and replacement valuation verified.";
                }
            }
            else
            {
                status = ClaimsReadinessStatus.ReadyForReview;
                reason = "Complete evidentiary package assembled for claims adjustor review.";
            }
        }

        return new ClaimsReadinessAssessmentDto
        {
            ShipmentId = shipmentId,
            TrackingNumber = trackingNumber,
            CarrierCode = carrierCode,
            FacilityId = facilityId,
            ClientId = clientId,
            ClaimType = claimType,
            ReadinessStatus = status,
            ReadinessScorePercent = scorePercent,
            HasHandoverEvidence = hasHandover,
            HasTransitScanHistory = hasTracking,
            HasObstructionEvidence = hasDisruption,
            HasThresholdBreach = hasThresholdBreach,
            HasFreightCostEvidence = hasFreightCost,
            HasValuationEvidence = hasValuation,
            HasPodEvidence = hasPod,
            MissingEvidenceItems = missingItems.AsReadOnly(),
            ReviewEligibilityReason = reason,
            AssessedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<ClaimsReadinessAssessmentDto>> GetFacilityClaimsReadinessBatchAsync(
        Guid facilityId,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        // Query active lost shipment alerts as candidates for claims readiness
        var query = _dbContext.CarrierLostShipmentAlerts
            .AsNoTracking()
            .Where(a => a.FacilityId == facilityId && a.AlertStatus == LostShipmentAlertStatus.Active);

        if (clientId.HasValue)
        {
            query = query.Where(a => a.ClientId == clientId.Value);
        }

        var alerts = await query.ToListAsync(cancellationToken);
        var assessments = new List<ClaimsReadinessAssessmentDto>(alerts.Count);

        foreach (var alert in alerts)
        {
            var assessment = await AssessShipmentClaimsReadinessAsync(alert.ShipmentId, ClaimType.Lost, cancellationToken);
            assessments.Add(assessment);
        }

        return assessments;
    }
}
