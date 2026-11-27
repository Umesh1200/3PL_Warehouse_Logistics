using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Immutable-keyed provenance and quarantine diagnostic record preserving original legacy country values,
/// normalization/inference lineage, historical preservation flags, and quarantine reasons.
/// </summary>
public class CountryNormalizationAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OriginalCountryValue { get; set; }
    public string? NormalizedCountryCode { get; set; }
    public CountryNormalizationStatus NormalizationStatus { get; set; }
    public bool PreservedHistoricalValue { get; set; }
    public bool WasInferredFromParent { get; set; }
    public string? InferenceSource { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public string CorrelationBatchId { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public string InitialCorrelationBatchId { get; set; } = string.Empty;
    public DateTime InitialRecordedAtUtc { get; set; } = DateTime.UtcNow;
}
