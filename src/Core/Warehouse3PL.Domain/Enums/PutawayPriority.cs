namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Execution priority for putaway operations.
/// </summary>
public enum PutawayPriority
{
    Normal = 1,
    High = 2,
    Expedite = 3 // e.g. Cold Chain, temperature-sensitive or rush goods
}

