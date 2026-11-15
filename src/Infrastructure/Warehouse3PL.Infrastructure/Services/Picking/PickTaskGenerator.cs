using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Picking;

/// <summary>
/// Generates executable RF pick tasks directly from active InventoryAllocation records.
/// Enforces duplicate task prevention and ensures cumulative task quantity does not exceed active allocation.
/// DB-SPEC-3PL-2026 Phase 4.3 Step 2.
/// </summary>
public class PickTaskGenerator : IPickTaskGenerator
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PickTaskGenerator> _logger;

    public PickTaskGenerator(
        IApplicationDbContext context,
        ILogger<PickTaskGenerator> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PickTaskGenerationResult> GenerateTasksForWaveAsync(
        PickWave wave,
        CancellationToken cancellationToken = default)
    {
        if (wave == null) throw new ArgumentNullException(nameof(wave));

        // Gather order IDs from wave
        var orderIds = wave.WaveOrders.Select(o => o.OutboundOrderId).Distinct().ToList();

        if (orderIds.Count == 0)
        {
            return new PickTaskGenerationResult
            {
                GeneratedTasks = [],
                ExistingTasks = [],
                Warnings = ["Wave contains no orders to generate pick tasks for."]
            };
        }

        // Fetch all active allocations for the wave's orders
        var allocationsQuery = _context.InventoryAllocations
            .Where(a => orderIds.Contains(a.OutboundOrderId) &&
                        (a.Status == AllocationStatus.Allocated || a.Status == AllocationStatus.PartiallyPicked) &&
                        !a.IsDeleted);

        // If wave has a specific Zone restriction, filter allocations by location zone
        if (wave.ZoneId.HasValue)
        {
            allocationsQuery = allocationsQuery.Where(a => a.Location.ZoneId == wave.ZoneId.Value);
        }

        var allocations = await allocationsQuery
            .Include(a => a.Location)
            .Include(a => a.Item)
            .ToListAsync(cancellationToken);

        // If wave has a specific TempClass restriction, filter allocations by item temperature
        if (wave.TempClass.HasValue)
        {
            allocations = allocations.Where(a => a.Item.StorageTempClass == wave.TempClass.Value).ToList();
        }

        return await GenerateTasksForAllocationsAsync(wave.Id, allocations, cancellationToken);
    }

    public async Task<PickTaskGenerationResult> GenerateTasksForAllocationsAsync(
        Guid pickWaveId,
        IReadOnlyList<InventoryAllocation> allocations,
        CancellationToken cancellationToken = default)
    {
        if (allocations == null || allocations.Count == 0)
        {
            return new PickTaskGenerationResult
            {
                GeneratedTasks = [],
                ExistingTasks = [],
                Warnings = ["No active allocations provided for pick task generation."]
            };
        }

        var allocationIds = allocations.Select(a => a.Id).ToList();

        // Query existing non-cancelled tasks tied to these allocations to prevent duplicate creation
        var existingTasks = await _context.PickTasks
            .Where(t => allocationIds.Contains(t.InventoryAllocationId) &&
                        t.Status != PickTaskStatus.Cancelled &&
                        !t.IsDeleted)
            .ToListAsync(cancellationToken);

        var existingTasksByAllocation = existingTasks
            .GroupBy(t => t.InventoryAllocationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var generatedTasks = new List<PickTask>();
        var warnings = new List<string>();

        foreach (var allocation in allocations)
        {
            var activeAllocatedQty = allocation.ActiveAllocatedQuantity;
            if (activeAllocatedQty <= 0)
            {
                warnings.Add($"Allocation '{allocation.Id}' has no active allocated quantity. Skipped.");
                continue;
            }

            existingTasksByAllocation.TryGetValue(allocation.Id, out var existingForAlloc);
            existingForAlloc ??= [];

            var alreadyTaskedQty = existingForAlloc.Sum(t => t.QuantityToPick);
            var remainingQtyToTask = activeAllocatedQty - alreadyTaskedQty;

            if (remainingQtyToTask <= 0)
            {
                _logger.LogDebug(
                    "Allocation '{AllocationId}' is already fully tasked (Active: {Active}, Tasked: {Tasked}). Skipping.",
                    allocation.Id, activeAllocatedQty, alreadyTaskedQty);
                continue;
            }

            // Create new PickTask bound to this allocation
            var task = new PickTask
            {
                Id = Guid.NewGuid(),
                ClientId = allocation.ClientId,
                FacilityId = allocation.FacilityId,
                PickWaveId = pickWaveId,
                OutboundOrderId = allocation.OutboundOrderId,
                OutboundOrderLineId = allocation.OutboundOrderLineId,
                InventoryAllocationId = allocation.Id,
                StockOnHandId = allocation.StockOnHandId,
                ItemId = allocation.ItemId,
                SourceLocationId = allocation.LocationId,
                LotId = allocation.LotId,
                ExpectedLpnCode = allocation.LpnCode,
                QuantityToPick = remainingQtyToTask,
                QuantityPicked = 0m,
                Status = PickTaskStatus.Pending,
                PickSequence = 0 // Will be set by SerpentineRouteOptimizer
            };

            // Enforce domain invariant: cumulative task quantity cannot exceed active allocation
            if (alreadyTaskedQty + task.QuantityToPick > activeAllocatedQty)
            {
                throw new DomainException(
                    $"Generated task quantity ({task.QuantityToPick}) plus existing ({alreadyTaskedQty}) " +
                    $"exceeds active allocated quantity ({activeAllocatedQty}) for Allocation '{allocation.Id}'.");
            }

            generatedTasks.Add(task);
        }

        _logger.LogInformation(
            "Task generation complete for Wave '{WaveId}'. Generated: {GenCount}, Existing: {ExistCount}, Total Units: {TotalUnits}",
            pickWaveId, generatedTasks.Count, existingTasks.Count, generatedTasks.Sum(t => t.QuantityToPick));

        return new PickTaskGenerationResult
        {
            GeneratedTasks = generatedTasks,
            ExistingTasks = existingTasks,
            Warnings = warnings
        };
    }
}

