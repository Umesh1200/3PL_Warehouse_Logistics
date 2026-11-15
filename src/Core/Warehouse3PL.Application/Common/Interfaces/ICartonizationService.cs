using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class CartonizationItemRequest
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }
    public decimal VolumeCm3 => LengthCm * WidthCm * HeightCm;
}

public class CartonizationRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? OutboundOrderId { get; set; }
    public List<CartonizationItemRequest> Items { get; set; } = [];
    public Guid? PreferredPackagingMaterialId { get; set; }
    public decimal VolumeUtilizationFactor { get; set; } = 0.85m;
}

public class CartonItemRecommendation
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }
}

public class PackageRecommendation
{
    public int PackageIndex { get; set; }
    public Guid PackagingMaterialId { get; set; }
    public string PackagingMaterialCode { get; set; } = string.Empty;
    public string PackagingMaterialName { get; set; } = string.Empty;
    public PackagingMaterialType MaterialType { get; set; }
    public decimal BoxLengthCm { get; set; }
    public decimal BoxWidthCm { get; set; }
    public decimal BoxHeightCm { get; set; }
    public decimal BoxMaxWeightKg { get; set; }
    public decimal TareWeightKg { get; set; }
    public decimal EstimatedNetWeightKg { get; set; }
    public decimal EstimatedGrossWeightKg { get; set; }
    public decimal EstimatedVolumeCm3 { get; set; }
    public decimal BoxCapacityVolumeCm3 { get; set; }
    public decimal VolumetricWeightKg { get; set; }
    public decimal VolumeUtilizationPercentage { get; set; }
    public List<CartonItemRecommendation> Items { get; set; } = [];
}

public class CartonizationRecommendation
{
    public bool IsSuccess { get; set; }
    public int RecommendedPackageCount => RecommendedPackages.Count;
    public List<PackageRecommendation> RecommendedPackages { get; set; } = [];
    public decimal TotalEstimatedGrossWeightKg => RecommendedPackages.Sum(p => p.EstimatedGrossWeightKg);
    public decimal TotalEstimatedVolumeCm3 => RecommendedPackages.Sum(p => p.EstimatedVolumeCm3);
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}

public interface ICartonizationService
{
    Task<CartonizationRecommendation> RecommendCartonizationAsync(
        CartonizationRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateCartonFitAsync(
        Guid packagingMaterialId,
        IEnumerable<CartonizationItemRequest> items,
        CancellationToken cancellationToken = default);
}

