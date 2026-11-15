using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class GetRatesForPackageRequest
{
    public Guid PackageId { get; set; }
    public string? PreferredCarrierCode { get; set; }
    public CarrierServiceLevel? PreferredServiceLevel { get; set; }
}

public class GenerateShippingLabelRequest
{
    public Guid PackageId { get; set; }
    public string CarrierCode { get; set; } = "SIM-FEDEX";
    public CarrierServiceLevel ServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public string LabelFormat { get; set; } = "ZPL";
    public string? IdempotencyKey { get; set; }
    public bool SimulateFailure { get; set; } = false;
    public Guid UserId { get; set; }
}

public class GenerateShippingLabelResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public Guid LabelId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; }
    public string LabelFormat { get; set; } = "ZPL";
    public string LabelData { get; set; } = string.Empty;
    public string? RoutingCode { get; set; }
    public decimal RateCharged { get; set; }
    public PackageStatus PackageStatus { get; set; }
    public ShippingLabelStatus LabelStatus { get; set; }
    public bool IsSimulated { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class VoidShippingLabelRequest
{
    public Guid LabelId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public bool SimulateFailure { get; set; } = false;
    public Guid UserId { get; set; }
}

public class VoidShippingLabelResult
{
    public bool IsSuccess { get; set; }
    public Guid LabelId { get; set; }
    public Guid PackageId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public ShippingLabelStatus LabelStatus { get; set; }
    public PackageStatus PackageStatus { get; set; }
    public string VoidConfirmationNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class ShippingLabelDto
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; }
    public string LabelFormat { get; set; } = string.Empty;
    public string LabelData { get; set; } = string.Empty;
    public string? RoutingCode { get; set; }
    public ShippingLabelStatus Status { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? PrintedAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
}

public interface IShippingLabelService
{
    Task<IReadOnlyList<CarrierRateQuote>> GetRatesForPackageAsync(
        GetRatesForPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<GenerateShippingLabelResult> GenerateLabelAsync(
        GenerateShippingLabelRequest request,
        CancellationToken cancellationToken = default);

    Task<VoidShippingLabelResult> VoidLabelAsync(
        VoidShippingLabelRequest request,
        CancellationToken cancellationToken = default);

    Task<ShippingLabelDto?> GetLabelAsync(
        Guid labelId,
        CancellationToken cancellationToken = default);

    Task<CarrierTrackingResponse> TrackPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);
}

