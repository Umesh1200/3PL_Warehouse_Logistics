using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.WebApi.Models;
using Warehouse3PL.Infrastructure.Services.Analytics;
namespace Warehouse3PL.WebApi.Controllers;
[Authorize]
[Route("api/v1/analytics/profitability")]
public sealed class ProfitabilityIntelligenceController(IProfitabilityIntelligenceService profitability, IFreightReconciliationService reconciliation, TransportationAnalyticsAuthorization authorization) : BaseApiController
{
    [HttpGet("contributions")]
    [HasPermission(Permissions.Analytics.ProfitabilityView)]
    public async Task<IActionResult> Contributions([FromQuery] TransportationAnalyticsQuery query, CancellationToken ct)
        => Ok(await profitability.GetReportAsync(query.Start,query.End,query.ClientId,query.FacilityId,ct));
    [HttpGet("shipments/{shipmentId:guid}")]
    [HasPermission(Permissions.Analytics.ProfitabilityView)]
    public async Task<IActionResult> Shipment(Guid shipmentId, [FromQuery] DateTimeOffset? asOfUtc, CancellationToken ct)
        { await authorization.RequireShipmentAsync(shipmentId,ct); return Ok(await reconciliation.GetShipmentFinancialReconciliationAsync(shipmentId,asOfUtc?.UtcDateTime,ct)); }
}

