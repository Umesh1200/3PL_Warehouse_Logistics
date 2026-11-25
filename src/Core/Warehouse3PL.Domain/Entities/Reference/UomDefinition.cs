using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// UN/ECE Recommendation 20 compliant Unit of Measure definition.
/// Platform-governed immutable reference catalog preserving canonical WMS base metrics
/// (KG for Mass, CM for Length, CM3 for Volume, EA for Count).
/// </summary>
public class UomDefinition
{
    public string UomCode { get; set; } = string.Empty;
    public string UneceRec20Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UomDimension Dimension { get; set; }
    public UomSystemOfMeasure SystemOfMeasure { get; set; }
    public bool IsCanonicalBase { get; set; }
    public int RoundingPrecision { get; set; } = 4;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UomConversion> ConversionsFrom { get; set; } = [];
    public ICollection<UomConversion> ConversionsTo { get; set; } = [];
}
