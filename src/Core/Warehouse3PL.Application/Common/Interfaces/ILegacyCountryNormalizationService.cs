using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Deterministic, reference-catalog-driven country normalization, safe inference, and backfill service.
/// Never guesses or defaults silently to any country, and preserves immutable historical snapshots.
/// </summary>
public interface ILegacyCountryNormalizationService
{
    Task<CountryNormalizationResolution> ResolveCountryAsync(
        string? rawCountry,
        CancellationToken cancellationToken = default);

    Task<LegacyCountryBackfillSummary> BackfillAllAsync(
        string? correlationBatchId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports sanitized provenance and quarantine audit snapshots in deterministic JSON format
    /// to support retention and pre-downgrade audit preservation workflows.
    /// </summary>
    Task<string> ExportProvenanceSnapshotJsonAsync(CancellationToken cancellationToken = default);
}
