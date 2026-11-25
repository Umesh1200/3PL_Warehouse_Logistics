using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Authoritative platform-governed physical conversion rule between two UOM definitions sharing the same dimension.
/// </summary>
public class UomConversion
{
    public string FromUomCode { get; set; } = string.Empty;
    public string ToUomCode { get; set; } = string.Empty;
    public UomDimension Dimension { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal InverseFactor { get; set; }
    public bool IsExact { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UomDefinition FromUom { get; set; } = null!;
    public UomDefinition ToUom { get; set; } = null!;
}
