using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class StartPackingSessionRequest
{
    public Guid FacilityId { get; set; }
    public Guid PackingStationLocationId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid OperatorUserId { get; set; }
}

public class PackingSessionResult
{
    public bool IsSuccess { get; set; }
    public Guid SessionId { get; set; }
    public PackingSessionStatus Status { get; set; }
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PackingStationLocationId { get; set; }
    public string StationLocationCode { get; set; } = string.Empty;
    public Guid OperatorUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public int ActivePackagesCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreatePackageRequest
{
    public Guid PackingSessionId { get; set; }
    public Guid? PackagingMaterialId { get; set; }
    public string? OverrideReason { get; set; }
    public Guid UserId { get; set; }
}

public class CreatePackageResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public Guid HandlingUnitId { get; set; }
    public string HandlingUnitLpn { get; set; } = string.Empty;
    public PackageStatus Status { get; set; }
    public Guid? PackagingMaterialId { get; set; }
    public string? PackagingMaterialCode { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal TareWeightKg { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PackItemRequest
{
    public Guid PackingSessionId { get; set; }
    public Guid PackageId { get; set; }
    public string ScannedToteLpn { get; set; } = string.Empty;
    public string ScannedItemBarcode { get; set; } = string.Empty;
    public string? ScannedLotNumber { get; set; }
    public decimal Quantity { get; set; }
    public Guid UserId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class PackItemResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public Guid PackageLineId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? LotNumber { get; set; }
    public decimal QuantityPacked { get; set; }
    public decimal RemainingToteQuantity { get; set; }
    public decimal RemainingOrderLineDemand { get; set; }
    public string SourceToteLpn { get; set; } = string.Empty;
    public string CartonLpn { get; set; } = string.Empty;
    public long InventoryTransactionId { get; set; }
    public bool IsOrderLineComplete { get; set; }
    public bool IsOrderFullyPacked { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ClosePackageRequest
{
    public Guid PackageId { get; set; }
    public decimal ScaleGrossWeightKg { get; set; }
    public Guid UserId { get; set; }
}

public class ClosePackageResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public decimal GrossWeightKg { get; set; }
    public decimal TareWeightKg { get; set; }
    public decimal NetWeightKg { get; set; }
    public decimal VolumetricWeightKg { get; set; }
    public PackageStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ReleaseToteRequest
{
    public string ToteLpn { get; set; } = string.Empty;
    public Guid FacilityId { get; set; }
    public Guid UserId { get; set; }
}

public class ReleaseToteResult
{
    public bool IsSuccess { get; set; }
    public Guid HandlingUnitId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public HandlingUnitStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CompletePackingSessionRequest
{
    public Guid PackingSessionId { get; set; }
    public Guid UserId { get; set; }
}

public class CompletePackingSessionResult
{
    public bool IsSuccess { get; set; }
    public Guid SessionId { get; set; }
    public PackingSessionStatus Status { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int TotalPackagesCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface IPackingExecutionService
{
    Task<PackingSessionResult> StartOrResumeSessionAsync(
        StartPackingSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<CreatePackageResult> CreatePackageAsync(
        CreatePackageRequest request,
        CancellationToken cancellationToken = default);

    Task<PackItemResult> PackItemAsync(
        PackItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ClosePackageResult> ClosePackageAsync(
        ClosePackageRequest request,
        CancellationToken cancellationToken = default);

    Task<ReleaseToteResult> ReleaseToteAsync(
        ReleaseToteRequest request,
        CancellationToken cancellationToken = default);

    Task<CompletePackingSessionResult> CompletePackingSessionAsync(
        CompletePackingSessionRequest request,
        CancellationToken cancellationToken = default);
}

