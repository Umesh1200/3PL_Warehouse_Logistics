using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.WebApi.Models;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Analytics - Transportation & Carrier SLA")]
[Route("api/v1/analytics/transportation")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class TransportationAnalyticsController(
    ICarrierScorecardService scorecards,
    IClaimsReadinessService claims,
    IFreightCostVarianceService freight,
    IFreightReconciliationService reconciliation,
    TransportationAnalyticsAuthorization authorization) : BaseApiController
{
    private const string CarrierPattern = @"^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$";

    [HttpGet("carriers/{carrierCode}/scorecard")]
    [HasPermission(Permissions.Analytics.TransportationView)]
    public async Task<ActionResult<CarrierScorecardDto>> GetScorecard(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] CarrierPerformanceQuery query, CancellationToken ct)
        => Ok(await scorecards.GetCarrierScorecardAsync(carrierCode, query.Start, query.End, query.FacilityId, query.ClientId, query.ServiceLevel, ct));

    [HttpGet("carriers/{carrierCode}/transit-time-distribution")]
    [HasPermission(Permissions.Analytics.TransportationView)]
    public async Task<ActionResult<TransitTimeDistributionDto>> GetTransitDistribution(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] TransitDistributionQuery query, CancellationToken ct)
        => Ok(await scorecards.GetTransitTimeDistributionAsync(carrierCode, query.Start, query.End, query.FacilityId, query.ClientId,
            query.ServiceLevel, query.DestinationCountry, query.DestinationState, ct));

    [HttpGet("carriers/{carrierCode}/exceptions")]
    [HasPermission(Permissions.Analytics.TransportationView)]
    public async Task<ActionResult<DeliveryExceptionIntelligenceDto>> GetExceptions(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] TransportationAnalyticsQuery query, CancellationToken ct)
        => Ok(await scorecards.GetDeliveryExceptionIntelligenceAsync(carrierCode, query.Start, query.End, query.FacilityId, query.ClientId, ct));

    [HttpGet("shipments/{shipmentId:guid}/claims-readiness")]
    [HasPermission(Permissions.Analytics.TransportationClaimsView)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimsReadinessAssessmentDto>> GetClaimsReadiness(
        Guid shipmentId, [FromQuery] ClaimsReadinessQuery query, CancellationToken ct)
        => Ok(await claims.AssessShipmentClaimsReadinessAsync(shipmentId, query.ClaimType, ct));

    [HttpGet("facilities/{facilityId:guid}/claims-readiness")]
    [HasPermission(Permissions.Analytics.TransportationClaimsView)]
    public async Task<ActionResult<PagedResult<ClaimsReadinessAssessmentDto>>> GetClaimsReadinessPage(
        Guid facilityId, [FromQuery] ClaimsReadinessPageQuery query, CancellationToken ct)
    {
        if (facilityId == Guid.Empty || (query.FacilityId.HasValue && query.FacilityId != facilityId))
            return BadRequest(new ProblemDetails { Status = 400, Title = "The facility filter must match the non-empty route facility." });
        return Ok(await claims.GetFacilityClaimsReadinessPageAsync(facilityId, query.ClientId, query.PageNumber, query.PageSize, ct));
    }

    [HttpGet("shipments/{shipmentId:guid}/financial-reconciliation")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReconciledShipmentFinancialsDto>> GetShipmentFinancials(
        Guid shipmentId, [FromQuery] DateTimeOffset? asOfUtc, CancellationToken ct)
    {
        await authorization.RequireShipmentAsync(shipmentId, ct);
        return Ok(await reconciliation.GetShipmentFinancialReconciliationAsync(shipmentId, asOfUtc?.UtcDateTime, ct));
    }

    [HttpGet("carriers/{carrierCode}/freight/variance")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    public async Task<ActionResult<FreightCostVarianceSummaryDto>> GetFreightVariance(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] FreightAnalyticsQuery query, CancellationToken ct)
    {
        var scope = await authorization.ResolveAsync(query.ClientId, query.FacilityId, ct);
        return Ok(await freight.GetFreightCostVarianceSummaryAsync(carrierCode, query.Start, query.End, scope.FacilityId, scope.ClientId, query.AsOfUtc?.UtcDateTime, ct));
    }

    [HttpGet("carriers/{carrierCode}/freight/profitability")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    public async Task<ActionResult<FreightProfitabilitySummaryDto>> GetFreightProfitability(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] FreightProfitabilityQuery query, CancellationToken ct)
    {
        var scope = await authorization.ResolveAsync(query.ClientId, query.FacilityId, ct);
        return Ok(await freight.GetFreightProfitabilitySummaryAsync(carrierCode, query.Start, query.End, scope.FacilityId, scope.ClientId,
            query.ServiceLevel, query.AsOfUtc?.UtcDateTime, ct));
    }

    [HttpGet("carriers/{carrierCode}/freight/accessorials")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    public async Task<ActionResult<AccessorialLeakageAnalysisDto>> GetAccessorials(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] FreightAnalyticsQuery query, CancellationToken ct)
    {
        var scope = await authorization.ResolveAsync(query.ClientId, query.FacilityId, ct);
        return Ok(await freight.GetAccessorialLeakageAnalysisAsync(carrierCode, query.Start, query.End, scope.FacilityId, scope.ClientId, query.AsOfUtc?.UtcDateTime, ct));
    }

    [HttpGet("carriers/{carrierCode}/freight/discrepancies")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    public async Task<ActionResult<PagedResult<CarrierInvoiceDiscrepancyDto>>> GetDiscrepancies(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] FreightDiscrepancyQuery query, CancellationToken ct)
    {
        var scope = await authorization.ResolveAsync(query.ClientId, query.FacilityId, ct);
        var rows = await freight.GetDiscrepancyManagementQueueAsync(carrierCode, query.Start, query.End, scope.FacilityId, scope.ClientId,
            query.StatusFilter, query.MinimumDiscrepancyUsd, query.AsOfUtc?.UtcDateTime, ct);
        var page = rows.OrderBy(r => r.ShipmentId).ThenBy(r => r.CarrierCostRecordId).ThenBy(r => r.InvoiceLineIdentifier)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Ok(new PagedResult<CarrierInvoiceDiscrepancyDto>(page, rows.Count, query.PageNumber, query.PageSize));
    }

    [HttpGet("carriers/{carrierCode}/freight/rate-card-compliance")]
    [HasPermission(Permissions.Analytics.TransportationFinancialView)]
    public async Task<ActionResult<RateCardComplianceAnalysisDto>> GetRateCardCompliance(
        [FromRoute, RegularExpression(CarrierPattern)] string carrierCode, [FromQuery] FreightAnalyticsQuery query, CancellationToken ct)
    {
        var scope = await authorization.ResolveAsync(query.ClientId, query.FacilityId, ct);
        return Ok(await freight.GetRateCardComplianceAnalysisAsync(carrierCode, query.Start, query.End, scope.FacilityId,
            query.AsOfUtc?.UtcDateTime, scope.ClientId, ct));
    }
}
