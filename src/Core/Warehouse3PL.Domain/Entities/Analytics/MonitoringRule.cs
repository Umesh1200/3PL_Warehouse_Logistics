using Warehouse3PL.Domain.Common;
namespace Warehouse3PL.Domain.Entities.Analytics;
public enum MonitoringMetric { StockAvailable, ShipmentTransitHours, SlaLateHours, FreightVariancePercent, DamagedStockPercent }
public enum MonitoringComparison { GreaterThan, LessThan }
public enum MonitoringSeverity { Low=1, Medium=2, High=3, Critical=4 }
public enum MonitoringAlertStatus { Active, Acknowledged, Resolved }
public sealed class MonitoringRule : AuditableEntity<Guid>, ITenantEntity
{
    public MonitoringRule() { Id=Guid.NewGuid(); }
    public Guid ClientId {get;set;}
    public Guid FacilityId {get;set;}
    public string Name {get;set;} = "";
    public MonitoringMetric Metric {get;set;}
    public MonitoringComparison Comparison {get;set;}
    public decimal Threshold {get;set;}
    public MonitoringSeverity Severity {get;set;}
    public bool Enabled {get;set;} = true;
}
public sealed class MonitoringAlert : AuditableEntity<Guid>, ITenantEntity
{
    public MonitoringAlert() { Id=Guid.NewGuid(); }
    public Guid ClientId {get;set;}
    public Guid FacilityId {get;set;}
    public Guid RuleId {get;set;}
    public string EvidenceKey {get;set;} = "";
    public decimal ObservedValue {get;set;}
    public string Explanation {get;set;} = "";
    public MonitoringSeverity Severity {get;set;}
    public MonitoringAlertStatus Status {get;set;}
    public DateTime FirstObservedAtUtc {get;set;}
    public DateTime LastObservedAtUtc {get;set;}
    public Guid? AcknowledgedBy {get;set;}
    public DateTime? AcknowledgedAtUtc {get;set;}
    public DateTime? ResolvedAtUtc {get;set;}
    public string ResolutionNotes {get;set;} = "";
}
