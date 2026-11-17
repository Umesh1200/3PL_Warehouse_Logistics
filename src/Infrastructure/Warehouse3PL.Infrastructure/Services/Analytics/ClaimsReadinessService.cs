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
    private readonly TransportationAnalyticsAuthorization _authorization;
    private readonly ILogger<ClaimsReadinessService> _logger;

    public ClaimsReadinessService(
        IApplicationDbContext dbContext,
        ILogger<ClaimsReadinessService> logger,
        TransportationAnalyticsAuthorization authorization)
    {
        _dbContext = dbContext;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<ClaimsReadinessAssessmentDto> AssessShipmentClaimsReadinessAsync(
        Guid shipmentId,
        ClaimType claimType = ClaimType.Lost,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _authorization.RequireShipmentAsync(shipmentId, cancellationToken);
        var scope = new TransportationQueryScope(shipment.ClientId, shipment.FacilityId, null);
        var compliance = await scope.Apply(_dbContext.CarrierSlaComplianceRecords.AsNoTracking())
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ShipmentId == shipmentId, cancellationToken);

        var alert = await scope.Apply(_dbContext.CarrierLostShipmentAlerts.AsNoTracking())
            .OrderByDescending(a => a.TriggeredAtUtc).ThenBy(a => a.Id)
            .FirstOrDefaultAsync(a => a.ShipmentId == shipmentId, cancellationToken);

        // Historical alerts remain obstruction evidence, but resolved/dismissed alerts cannot
        // establish a current inactivity breach for a new lost-shipment assessment.
        var hasThresholdBreach = await scope.Apply(_dbContext.CarrierLostShipmentAlerts.AsNoTracking())
            .AnyAsync(a => a.ShipmentId == shipmentId && a.AlertStatus == LostShipmentAlertStatus.Active
                && a.PolicyThresholdHours > 0m && a.HoursWithoutScanMovement >= a.PolicyThresholdHours, cancellationToken);

        var trackingScansCount = await scope.Apply(_dbContext.ShipmentTrackingEvents.AsNoTracking())
            .AsNoTracking()
            .CountAsync(t => t.ShipmentId == shipmentId, cancellationToken);

        var exceptionsCount = await scope.Apply(_dbContext.ShipmentDeliveryExceptions.AsNoTracking())
            .AsNoTracking()
            .CountAsync(e => e.ShipmentId == shipmentId && e.Status != DeliveryExceptionStatus.Voided
                && (claimType != ClaimType.Damaged || e.ExceptionType == DeliveryExceptionType.PackageDamagedInTransit), cancellationToken);

        var costQuery = scope.Apply(_dbContext.CarrierCostRecords.AsNoTracking())
            .Where(c => c.ShipmentId == shipmentId);
        var costRecord = await costQuery.OrderByDescending(c => c.CostAssessedAtUtc).ThenBy(c => c.Id)
            .FirstOrDefaultAsync(c => c.ShipmentId == shipmentId, cancellationToken);

        var hasPod = await scope.Apply(_dbContext.ProofOfDeliveries.AsNoTracking())
            .AnyAsync(p => p.ShipmentId == shipmentId && !p.IsSuperseded && p.IsStorageFinalized, cancellationToken);

        var carrierCode = compliance?.CarrierCode ?? alert?.CarrierCode ?? costRecord?.CarrierCode ?? shipment.CarrierCode;
        var trackingNumber = compliance?.TrackingNumber ?? alert?.TrackingNumber ?? costRecord?.TrackingNumber ?? "UNKNOWN";
        var facilityId = shipment.FacilityId;
        var clientId = shipment.ClientId;

        // Evidence Dimension Checklist
        bool hasHandover = compliance != null && compliance.DispatchedAtUtc != default;
        bool hasTracking = trackingScansCount > 0;
        bool hasDisruption = exceptionsCount > 0 || (claimType != ClaimType.Damaged && alert != null);
        bool hasFreightCost = await costQuery.AnyAsync(c => c.TotalCarrierCost > 0m, cancellationToken);
        // Freight charges and insurance premiums do not establish the value of the goods.
        // No authoritative declared/replacement valuation is persisted in the current model;
        // leave this mandatory evidence missing instead of claiming it was verified.
        bool hasValuation = false;

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
        var scope = await _authorization.ResolveAsync(clientId, facilityId, cancellationToken);
        clientId = scope.ClientId;
        // Query active lost shipment alerts as candidates for claims readiness
        var query = _dbContext.CarrierLostShipmentAlerts
            .AsNoTracking()
            .Where(a => a.FacilityId == facilityId && a.AlertStatus == LostShipmentAlertStatus.Active);

        if (clientId.HasValue)
        {
            query = query.Where(a => a.ClientId == clientId.Value);
        }

        var alerts = await scope.Apply(query).ToListAsync(cancellationToken);
        var assessments = new List<ClaimsReadinessAssessmentDto>(alerts.Count);

        foreach (var alert in alerts)
        {
            var assessment = await AssessShipmentClaimsReadinessAsync(alert.ShipmentId, ClaimType.Lost, cancellationToken);
            assessments.Add(assessment);
        }

        return assessments;
    }

    public async Task<PagedResult<ClaimsReadinessAssessmentDto>> GetFacilityClaimsReadinessPageAsync(
        Guid facilityId, Guid? clientId = null, int pageNumber = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber is < 1 or > 1000000 || pageSize is < 1 or > 200)
            throw new Warehouse3PL.Domain.Exceptions.ValidationException("Pagination", "Use pageNumber 1..1000000 and pageSize 1..200.");
        var scope = await _authorization.ResolveAsync(clientId, facilityId, cancellationToken);
        var shipments = scope.Apply(_dbContext.Shipments.AsNoTracking());
        var candidates = scope.Apply(_dbContext.CarrierLostShipmentAlerts.AsNoTracking())
            .Where(a => a.FacilityId == facilityId && a.AlertStatus == LostShipmentAlertStatus.Active &&
                shipments.Any(s => s.Id == a.ShipmentId && s.ClientId == a.ClientId && s.FacilityId == a.FacilityId))
            .Select(a => a.ShipmentId).Distinct();
        var count = await candidates.CountAsync(cancellationToken);
        var ids = await candidates.OrderBy(id => id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var results = new List<ClaimsReadinessAssessmentDto>(ids.Count);
        foreach (var id in ids) results.Add(await AssessShipmentClaimsReadinessAsync(id, ClaimType.Lost, cancellationToken));
        return new(results, count, pageNumber, pageSize);
    }
}
