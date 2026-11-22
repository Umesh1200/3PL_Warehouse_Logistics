using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Packing.DTOs;

public class PackingSessionSummaryDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackingStationLocationId { get; set; }
    public string PackingStationLocationCode { get; set; } = string.Empty;
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OperatorUserId { get; set; }
    public string OperatorEmail { get; set; } = string.Empty;
    public PackingSessionStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ActivePackagesCount { get; set; }
    public string? Notes { get; set; }
}

public class PackingSessionOrderLineDto
{
    public Guid OrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal QuantityPacked { get; set; }
    public decimal RemainingToPack { get; set; }
    public OutboundOrderLineStatus LineStatus { get; set; }
}

public class PackingToteSummaryDto
{
    public Guid HandlingUnitId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public HandlingUnitStatus Status { get; set; }
    public string? LocationCode { get; set; }
    public decimal TotalQuantity { get; set; }
}

public class PackageSummaryDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? ShipmentId { get; set; }
    public Guid HandlingUnitId { get; set; }
    public string HandlingUnitLpn { get; set; } = string.Empty;
    public Guid? PackagingMaterialId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public int CartonSequence { get; set; }
    public decimal GrossWeightKg { get; set; }
    public decimal TareWeightKg { get; set; }
    public decimal NetWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal VolumetricWeightKg { get; set; }
    public string? CarrierCode { get; set; }
    public string? TrackingNumber { get; set; }
    public PackageStatus Status { get; set; }
    public Guid? PackedByUserId { get; set; }
    public DateTime? PackedAtUtc { get; set; }
    public bool HasActiveLabel { get; set; }
    public Guid? LatestLabelId { get; set; }
}

public class PackingSessionDetailDto : PackingSessionSummaryDto
{
    public List<PackingSessionOrderLineDto> OrderLines { get; set; } = [];
    public List<PackageSummaryDto> Packages { get; set; } = [];
    public List<PackingToteSummaryDto> PickedTotes { get; set; } = [];
}

public static class PackageMappings
{
    public static PackageSummaryDto ToSummaryDto(Package p)
    {
        var activeLabel = p.ShippingLabels?
            .Where(l => l.Status != ShippingLabelStatus.Voided)
            .OrderByDescending(l => l.GeneratedAtUtc)
            .FirstOrDefault();

        return new PackageSummaryDto
        {
            Id = p.Id,
            ClientId = p.ClientId,
            FacilityId = p.FacilityId,
            OutboundOrderId = p.OutboundOrderId,
            OrderNumber = p.OutboundOrder?.OrderNumber ?? string.Empty,
            ShipmentId = p.ShipmentId,
            HandlingUnitId = p.HandlingUnitId,
            HandlingUnitLpn = p.HandlingUnit?.LpnCode ?? string.Empty,
            PackagingMaterialId = p.PackagingMaterialId,
            PackageNumber = p.PackageNumber,
            CartonSequence = p.CartonSequence,
            GrossWeightKg = p.GrossWeightKg,
            TareWeightKg = p.TareWeightKg,
            NetWeightKg = p.NetWeightKg,
            LengthCm = p.LengthCm,
            WidthCm = p.WidthCm,
            HeightCm = p.HeightCm,
            VolumetricWeightKg = p.VolumetricWeightKg,
            CarrierCode = p.CarrierCode,
            TrackingNumber = p.TrackingNumber,
            Status = p.Status,
            PackedByUserId = p.PackedByUserId,
            PackedAtUtc = p.PackedAtUtc,
            HasActiveLabel = activeLabel != null,
            LatestLabelId = activeLabel?.Id
        };
    }
}
