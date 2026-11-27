using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public sealed record CountryNormalizationResolution(
    bool IsResolved,
    string? CanonicalCountryCode,
    CountryNormalizationStatus Status,
    bool WasInferredFromParent,
    string? InferenceSource,
    string Reason);

public sealed record LegacyCountryBackfillSummary(
    string CorrelationBatchId,
    int TotalEvaluatedRecords,
    int NormalizedMutatedCount,
    int UnchangedCanonicalCount,
    int PreservedHistoricalCount,
    int InferredCount,
    int QuarantinedCount);

public sealed record CountryNormalizationQuarantineItemDto(
    Guid AuditId,
    string EntityType,
    Guid EntityId,
    string FieldName,
    string? OriginalCountryValue,
    string? NormalizedCountryCode,
    CountryNormalizationStatus NormalizationStatus,
    bool PreservedHistoricalValue,
    bool WasInferredFromParent,
    string? InferenceSource,
    string Reason,
    Guid? FacilityId,
    string? FacilityCode,
    Guid? ClientId,
    string CorrelationBatchId,
    DateTime RecordedAtUtc,
    string ReasonCode = "");

public sealed record PagedNormalizationQuarantineResponse(
    IReadOnlyList<CountryNormalizationQuarantineItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages,
    int PendingCount,
    int NormalizedCount);
