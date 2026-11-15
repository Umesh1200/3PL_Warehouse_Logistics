using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service assessing evidentiary completeness for carrier loss, damage, or SLA breach claims.
/// Operates as an objective evidence audit; does not initiate legal loss or automated billing debits.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public interface IClaimsReadinessService
{
    Task<ClaimsReadinessAssessmentDto> AssessShipmentClaimsReadinessAsync(
        Guid shipmentId,
        ClaimType claimType = ClaimType.Lost,
        CancellationToken cancellationToken = default);

    ClaimsReadinessAssessmentDto EvaluateReadinessCriteria(
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
        ClaimType claimType = ClaimType.Lost);

    Task<IReadOnlyList<ClaimsReadinessAssessmentDto>> GetFacilityClaimsReadinessBatchAsync(
        Guid facilityId,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);
}
