using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.WebApi.Models;
namespace Warehouse3PL.WebApi.Controllers;
[Authorize,Route("api/v1/analytics/monitoring")]
public sealed class MonitoringController(IMonitoringService monitoring) : BaseApiController
{
    [HttpGet("rules"),HasPermission(Permissions.Analytics.MonitoringView)]
    public async Task<IActionResult> Rules([FromQuery] TransportationScopeQuery query,CancellationToken ct)=>Ok(await monitoring.GetRulesAsync(query.ClientId,query.FacilityId,ct));
    [HttpPost("rules"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Create(MonitoringRuleRequest request,CancellationToken ct)=>Ok(await monitoring.CreateRuleAsync(request,ct));
    [HttpPut("rules/{id:guid}"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Update(Guid id,MonitoringRuleRequest request,CancellationToken ct)=>Ok(await monitoring.UpdateRuleAsync(id,request,ct));
    [HttpPut("rules/{id:guid}/enabled"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Enabled(Guid id,[FromBody] bool enabled,CancellationToken ct)=>Ok(await monitoring.SetEnabledAsync(id,enabled,ct));
    [HttpPost("rules/{id:guid}/evaluate"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Evaluate(Guid id,CancellationToken ct)=>Ok(await monitoring.EvaluateAsync(id,ct));
    [HttpGet("alerts"),HasPermission(Permissions.Analytics.MonitoringView)]
    public async Task<IActionResult> Alerts([FromQuery] TransportationScopeQuery query,int page=1,int size=50,CancellationToken ct=default)=>Ok(await monitoring.GetAlertsAsync(query.ClientId,query.FacilityId,page,size,ct));
    [HttpPost("alerts/{id:guid}/acknowledge"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Acknowledge(Guid id,CancellationToken ct)=>Ok(await monitoring.TransitionAsync(id,false,"",ct));
    [HttpPost("alerts/{id:guid}/resolve"),HasPermission(Permissions.Analytics.MonitoringManage)]
    public async Task<IActionResult> Resolve(Guid id,[FromBody] ResolutionRequest request,CancellationToken ct)=>Ok(await monitoring.TransitionAsync(id,true,request.Notes??"",ct));
    public sealed record ResolutionRequest(string? Notes);
}
