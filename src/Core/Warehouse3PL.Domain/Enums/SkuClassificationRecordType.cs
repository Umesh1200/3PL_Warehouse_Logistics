namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Differentiates operational SKU velocity tracking records from Pareto ABC inventory stratification runs.
/// Guarantees that continuous velocity profiling never overwrites or collides with versioned ABC classifications.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum SkuClassificationRecordType
{
    VelocityOperational = 1,
    AbcParetoStratification = 2
}

