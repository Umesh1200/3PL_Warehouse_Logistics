using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;

public class ShipmentPackageSummaryDto
{
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public decimal GrossWeightKg { get; set; }
    public PackageStatus Status { get; set; }
    public Guid OutboundOrderId { get; set; }
}

public class ShipmentDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; }
    public ShipmentStatus Status { get; set; }
    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public string? BillOfLadingNumber { get; set; }
    public int TotalPackagesCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public List<ShipmentPackageSummaryDto> Packages { get; set; } = [];
}

public class BillOfLadingDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public string BolNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    public string? TrailerNumber { get; set; }
    public string? SealNumber { get; set; }
    public int TotalPackageCount { get; set; }
    public decimal TotalGrossWeightKg { get; set; }
    public decimal TotalVolumeCm3 { get; set; }
    public string ShipperName { get; set; } = string.Empty;
    public string ShipperAddressSnapshot { get; set; } = string.Empty;
    public string ConsigneeName { get; set; } = string.Empty;
    public string ConsigneeAddressSnapshot { get; set; } = string.Empty;
    public string? PackageTrackingNumbers { get; set; }
    public string? SpecialInstructions { get; set; }
    public string DocumentSnapshotJson { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public bool IsVoided { get; set; }
}

