using Warehouse3PL.Domain.Entities.Analytics;
namespace Warehouse3PL.Application.Common.Interfaces;
public sealed record MonitoringRuleRequest(Guid ClientId,Guid FacilityId,string Name,MonitoringMetric Metric,MonitoringComparison Comparison,decimal Threshold,MonitoringSeverity Severity,bool Enabled=true);
public sealed record MonitoringEvaluationResult(int Observations,int Breaches,int Created,int Reopened,string EvidenceBasis);
public sealed record MonitoringAlertPage(IReadOnlyList<MonitoringAlert> Items,int TotalCount,int PageNumber,int PageSize);
public interface IMonitoringService
{
    Task<MonitoringRule> CreateRuleAsync(MonitoringRuleRequest request,CancellationToken ct=default);
    Task<MonitoringRule> SetEnabledAsync(Guid id,bool enabled,CancellationToken ct=default);
    Task<MonitoringRule> UpdateRuleAsync(Guid id,MonitoringRuleRequest request,CancellationToken ct=default);
    Task<IReadOnlyList<MonitoringRule>> GetRulesAsync(Guid? clientId,Guid? facilityId,CancellationToken ct=default);
    Task<MonitoringEvaluationResult> EvaluateAsync(Guid id,CancellationToken ct=default);
    Task<MonitoringAlertPage> GetAlertsAsync(Guid? clientId,Guid? facilityId,int page,int size,CancellationToken ct=default);
    Task<MonitoringAlert> TransitionAsync(Guid id,bool resolve,string notes,CancellationToken ct=default);
}
