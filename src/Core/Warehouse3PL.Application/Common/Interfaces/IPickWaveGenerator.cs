using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class PickWaveGenerationRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public PickWaveType WaveType { get; set; } = PickWaveType.BatchPick;
    public int? Priority { get; set; }
    public Guid? ZoneId { get; set; }
    public StorageTempClass? TempClass { get; set; }
    public IReadOnlyList<Guid>? OrderIds { get; set; }
    public int? MaxOrders { get; set; }
    public string? WaveNumber { get; set; }
}

public class PickWaveGenerationResult
{
    public PickWave Wave { get; set; } = null!;
    public IReadOnlyList<PickTask> Tasks { get; set; } = [];
    public PickRouteOptimizationResult Route { get; set; } = null!;
    public int OrdersCount => Wave?.WaveOrders?.Count ?? 0;
    public decimal TotalUnits => Tasks?.Sum(t => t.QuantityToPick) ?? 0m;
}

public interface IPickWaveGenerator
{
    Task<PickWaveGenerationResult> GenerateWaveAsync(
        PickWaveGenerationRequest request,
        CancellationToken cancellationToken = default);
}

