using Warehouse3PL.Domain.Common;
namespace Warehouse3PL.Domain.Entities.Analytics;
/// <summary>Immutable ingestion arguments and resolved projection effects; canonical sources are never reset by replay.</summary>
public sealed class AnalyticsIngestionJournal : BaseEntity<Guid>
{
    public AnalyticsIngestionJournal(){Id=Guid.NewGuid();}
    public string ProjectionName {get;set;}="";
    public string EventKind {get;set;}="";
    public Guid EventId {get;set;}
    public long SequenceNumber {get;set;}
    public DateTime EffectiveAtUtc {get;set;}
    public long IngestionOrdinal {get;set;}
    public int SchemaVersion {get;set;}=1;
    public string ArgumentsJson {get;set;}="";
    public string EffectsJson {get;set;}="";
    public string PayloadHash {get;set;}="";
}
