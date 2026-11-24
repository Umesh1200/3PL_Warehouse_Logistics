using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class CreateShipmentRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public Guid? StagingLocationId { get; set; }
    public Guid UserId { get; set; }
}

public class CreateShipmentResult
{
    public bool IsSuccess { get; set; }
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; }
    public int TotalPackagesCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AddPackageToShipmentRequest
{
    public Guid ShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public Guid UserId { get; set; }
}

public class AddPackageToShipmentResult
{
    public bool IsSuccess { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public int TotalPackagesCount { get; set; }
    public decimal TotalGrossWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RemovePackageFromShipmentRequest
{
    public Guid ShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

public class RemovePackageFromShipmentResult
{
    public bool IsSuccess { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public int TotalPackagesCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StagePackageRequest
{
    public Guid PackageId { get; set; }
    public Guid StagingLocationId { get; set; }
    public Guid UserId { get; set; }
}

public class StagePackageResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public PackageStatus PackageStatus { get; set; }
    public string StagingLocationCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class LoadPackageRequest
{
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }
    public string? TrailerNumber { get; set; }
    public Guid? DockLocationId { get; set; }
    public Guid UserId { get; set; }
}

public class LoadPackageResult
{
    public bool IsSuccess { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public PackageStatus PackageStatus { get; set; }
    public ShipmentStatus? ShipmentStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class GenerateBolRequest
{
    public Guid ShipmentId { get; set; }
    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public string? SpecialInstructions { get; set; }
    public Guid UserId { get; set; }
}

public class GenerateBolResult
{
    public bool IsSuccess { get; set; }
    public Guid BolId { get; set; }
    public string BolNumber { get; set; } = string.Empty;
    public Guid ShipmentId { get; set; }
    public int TotalPackageCount { get; set; }
    public decimal TotalGrossWeightKg { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public string DocumentSnapshotJson { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ConfirmDispatchCustodyRequest
{
    public Guid ShipmentId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string? DriverLicenseNumber { get; set; }
    public string DriverSignature { get; set; } = string.Empty;
    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public Guid AuthorizedUserId { get; set; }
    public Guid? AssignedDriverUserId { get; set; }
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ConfirmDispatchCustodyResult
{
    public bool IsSuccess { get; set; }
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; }
    public DateTime DispatchedAtUtc { get; set; }
    public int TotalPackagesDispatched { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public Guid? AssignedDriverUserId { get; set; }
    public string? BillOfLadingNumber { get; set; }
    public List<Guid> CompletedOrderIds { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class VoidBolRequest
{
    public Guid BolId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

public class VoidBolResult
{
    public bool IsSuccess { get; set; }
    public Guid BolId { get; set; }
    public string BolNumber { get; set; } = string.Empty;
    public Guid ShipmentId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface IShipmentConsolidationService
{
    Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CancellationToken cancellationToken = default);

    Task<AddPackageToShipmentResult> AddPackageToShipmentAsync(
        AddPackageToShipmentRequest request,
        CancellationToken cancellationToken = default);

    Task<RemovePackageFromShipmentResult> RemovePackageFromShipmentAsync(
        RemovePackageFromShipmentRequest request,
        CancellationToken cancellationToken = default);

    Task<StagePackageResult> StagePackageAsync(
        StagePackageRequest request,
        CancellationToken cancellationToken = default);

    Task<LoadPackageResult> LoadPackageAsync(
        LoadPackageRequest request,
        CancellationToken cancellationToken = default);

    Task<GenerateBolResult> GenerateBillOfLadingAsync(
        GenerateBolRequest request,
        CancellationToken cancellationToken = default);

    Task<VoidBolResult> VoidBillOfLadingAsync(
        VoidBolRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfirmDispatchCustodyResult> ConfirmDispatchAsync(
        ConfirmDispatchCustodyRequest request,
        CancellationToken cancellationToken = default);
}

