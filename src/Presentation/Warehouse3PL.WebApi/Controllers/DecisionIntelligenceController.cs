using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.WebApi.Models;
namespace Warehouse3PL.WebApi.Controllers;
[Authorize,Route("api/v1/analytics/decisions")]
public sealed class DecisionIntelligenceController(IDecisionIntelligenceService decisions) : BaseApiController
{
    [HttpGet("facilities/{facilityId:guid}/labor"),HasPermission(Permissions.Analytics.DecisionLaborView)]
    public async Task<IActionResult> Labor(Guid facilityId,[FromQuery] TransportationAnalyticsQuery query,CancellationToken ct)
    {
        if(query.ClientId.HasValue || query.FacilityId.HasValue && query.FacilityId!=facilityId)
            throw new ValidationException("scope","Labor advice is facility-wide and staff-only; omit clientId and use a matching facility filter.");
        return Ok(await decisions.GetLaborAdviceAsync(facilityId,query.Start,query.End,ct));
    }
    [HttpGet("carriers"),HasPermission(Permissions.Analytics.DecisionTransportationView)]
    public async Task<IActionResult> Carriers([FromQuery] CarrierDecisionQuery query,CancellationToken ct)
        => Ok(await decisions.GetCarrierAdviceAsync(query.CarrierCodes,query.Start,query.End,query.ClientId,query.FacilityId,query.ServiceLevel,ct));
    public sealed class CarrierDecisionQuery : TransportationAnalyticsQuery
    {
        public string[] CarrierCodes {get;set;}=[];
        public CarrierServiceLevel? ServiceLevel {get;set;}
    }
}
