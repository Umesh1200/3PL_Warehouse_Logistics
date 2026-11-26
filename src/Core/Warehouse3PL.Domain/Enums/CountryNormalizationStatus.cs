namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Tracks deterministic ISO 3166-1 alpha-2 country normalization status for legacy and operational records.
/// </summary>
public enum CountryNormalizationStatus
{
    Normalized = 1,
    PendingNormalization = 2
}
