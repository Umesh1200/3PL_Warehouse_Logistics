namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// SKU turnover velocity classification for ABC slotting algorithms.
/// </summary>
public enum VelocityClass
{
    A = 1, // High turnover / fast moving
    B = 2, // Medium turnover
    C = 3  // Low turnover / slow moving
}

