using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Application.Common.Interfaces;

public class PickTaskGenerationResult
{
    public IReadOnlyList<PickTask> GeneratedTasks { get; set; } = [];
    public IReadOnlyList<PickTask> ExistingTasks { get; set; } = [];
    public int TotalTasksCount => GeneratedTasks.Count + ExistingTasks.Count;
    public decimal TotalQuantityToPick => GeneratedTasks.Sum(t => t.QuantityToPick) + ExistingTasks.Sum(t => t.QuantityToPick);
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public interface IPickTaskGenerator
{
    Task<PickTaskGenerationResult> GenerateTasksForWaveAsync(
        PickWave wave,
        CancellationToken cancellationToken = default);

    Task<PickTaskGenerationResult> GenerateTasksForAllocationsAsync(
        Guid pickWaveId,
        IReadOnlyList<InventoryAllocation> allocations,
        CancellationToken cancellationToken = default);
}

