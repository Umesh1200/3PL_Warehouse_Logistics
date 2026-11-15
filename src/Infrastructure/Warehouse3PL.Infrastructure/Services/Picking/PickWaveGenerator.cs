using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Picking;

/// <summary>
/// Coordinated outbound fulfillment wave generation engine.
/// Selects eligible allocated orders, enforces multi-tenancy and thermal boundaries, prevents conflicting active wave assignments,
/// coordinates task generation from active allocations, and executes serpentine route optimization.
/// DB-SPEC-3PL-2026 Phase 4.3 Step 2.
/// </summary>
public class PickWaveGenerator : IPickWaveGenerator
{
    private readonly IApplicationDbContext _context;
    private readonly IPickTaskGenerator _taskGenerator;
    private readonly IPickRouteOptimizer _routeOptimizer;
    private readonly ILogger<PickWaveGenerator> _logger;

    public PickWaveGenerator(
        IApplicationDbContext context,
        IPickTaskGenerator taskGenerator,
        IPickRouteOptimizer routeOptimizer,
        ILogger<PickWaveGenerator> logger)
    {
        _context = context;
        _taskGenerator = taskGenerator;
        _routeOptimizer = routeOptimizer;
        _logger = logger;
    }

    public async Task<PickWaveGenerationResult> GenerateWaveAsync(
        PickWaveGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (request.ClientId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ClientId), "ClientId must be a valid non-empty GUID.");
        }

        if (request.FacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.FacilityId), "FacilityId must be a valid non-empty GUID.");
        }

        if (request.WaveType == PickWaveType.ZonePick && !request.ZoneId.HasValue)
        {
            throw new ValidationException(nameof(request.ZoneId), "ZoneId is required when generating a ZonePick wave.");
        }

        if (request.WaveType == PickWaveType.DiscreteOrder && request.OrderIds?.Count > 1)
        {
            throw new ValidationException(nameof(request.OrderIds),
                "DiscreteOrder wave type only supports a single order per wave.");
        }

        // 1. Identify orders already assigned to conflicting active waves (Planning, Released, InProgress)
        var activeWaveOrderIds = await _context.PickWaveOrders
            .Where(pwo => (pwo.PickWave.Status == PickWaveStatus.Planning ||
                           pwo.PickWave.Status == PickWaveStatus.Released ||
                           pwo.PickWave.Status == PickWaveStatus.InProgress) &&
                          !pwo.PickWave.IsDeleted)
            .Select(pwo => pwo.OutboundOrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // 2. Query candidate orders with active allocations
        var ordersQuery = _context.OutboundOrders
            .Where(o => o.ClientId == request.ClientId &&
                        o.FacilityId == request.FacilityId &&
                        !o.IsDeleted &&
                        o.Status != OutboundOrderStatus.OnHold &&
                        o.Status != OutboundOrderStatus.Cancelled &&
                        o.Status != OutboundOrderStatus.Completed &&
                        o.Status != OutboundOrderStatus.Draft);

        if (request.OrderIds != null && request.OrderIds.Count > 0)
        {
            // Explicit order list requested
            ordersQuery = ordersQuery.Where(o => request.OrderIds.Contains(o.Id));
        }

        var candidateOrders = await ordersQuery
            .Include(o => o.Allocations)
                .ThenInclude(a => a.Item)
            .Include(o => o.Allocations)
                .ThenInclude(a => a.Location)
            .ToListAsync(cancellationToken);

        // Validate explicit requested orders against active waves and eligibility
        if (request.OrderIds != null && request.OrderIds.Count > 0)
        {
            foreach (var requestedId in request.OrderIds)
            {
                var matched = candidateOrders.FirstOrDefault(o => o.Id == requestedId);
                if (matched == null)
                {
                    // Check if order exists in another tenant or facility
                    var orderAnywhere = await _context.OutboundOrders
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(o => o.Id == requestedId, cancellationToken);

                    if (orderAnywhere != null && orderAnywhere.ClientId != request.ClientId)
                    {
                        throw new DomainException($"Cross-tenant order '{orderAnywhere.OrderNumber}' cannot be added to wave.");
                    }

                    if (orderAnywhere != null && orderAnywhere.FacilityId != request.FacilityId)
                    {
                        throw new DomainException($"Cross-facility order '{orderAnywhere.OrderNumber}' cannot be added to wave.");
                    }

                    throw new DomainException($"Order ID '{requestedId}' is not eligible for wave generation (may be OnHold, Cancelled, Completed, or non-existent).");
                }

                if (activeWaveOrderIds.Contains(requestedId))
                {
                    throw new InvalidDomainStateException(
                        $"Order '{matched.OrderNumber}' is already assigned to an active pick wave.");
                }
            }
        }
        else
        {
            // Filter out orders that are already assigned to active waves
            candidateOrders = candidateOrders
                .Where(o => !activeWaveOrderIds.Contains(o.Id))
                .ToList();
        }

        // Must have active allocations
        candidateOrders = candidateOrders
            .Where(o => o.Allocations.Any(a => a.ActiveAllocatedQuantity > 0 &&
                                              (a.Status == AllocationStatus.Allocated || a.Status == AllocationStatus.PartiallyPicked) &&
                                              !a.IsDeleted))
            .ToList();

        // 3. Thermal zone segregation
        if (request.TempClass.HasValue)
        {
            candidateOrders = candidateOrders
                .Where(o => o.Allocations.Any(a => a.Item.StorageTempClass == request.TempClass.Value &&
                                                  a.ActiveAllocatedQuantity > 0))
                .ToList();
        }

        // 4. Warehouse zone segregation (for ZonePick)
        if (request.ZoneId.HasValue)
        {
            candidateOrders = candidateOrders
                .Where(o => o.Allocations.Any(a => a.Location.ZoneId == request.ZoneId.Value &&
                                                  a.ActiveAllocatedQuantity > 0))
                .ToList();
        }

        if (candidateOrders.Count == 0)
        {
            throw new DomainException("No eligible allocated orders found matching the wave generation criteria.");
        }

        // 5. Order Priority & Date Sorting
        var prioritizedOrders = candidateOrders
            .OrderByDescending(o => o.Priority)
            .ThenBy(o => o.RequiredShipDateUtc ?? DateTime.MaxValue)
            .ThenBy(o => o.OrderDateUtc)
            .ToList();

        // Limit order count based on wave type & max orders
        int maxOrdersToTake = request.WaveType switch
        {
            PickWaveType.DiscreteOrder => 1,
            _ => request.MaxOrders.GetValueOrDefault(20)
        };

        var selectedOrders = prioritizedOrders.Take(maxOrdersToTake).ToList();

        // 6. Build PickWave Aggregate
        var waveNumber = string.IsNullOrWhiteSpace(request.WaveNumber)
            ? $"WAVE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}"
            : request.WaveNumber.Trim();

        var wave = new PickWave
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            FacilityId = request.FacilityId,
            WaveNumber = waveNumber,
            WaveType = request.WaveType,
            Status = PickWaveStatus.Planning,
            Priority = request.Priority ?? selectedOrders.Max(o => o.Priority),
            ZoneId = request.ZoneId,
            TempClass = request.TempClass
        };

        // Add WaveOrders
        int seq = 1;
        foreach (var order in selectedOrders)
        {
            wave.WaveOrders.Add(new PickWaveOrder
            {
                Id = Guid.NewGuid(),
                PickWaveId = wave.Id,
                OutboundOrderId = order.Id,
                SequenceNumber = seq++,
                PickWave = wave,
                OutboundOrder = order
            });
        }

        // 7. Generate Tasks from Active Allocations
        var taskGenResult = await _taskGenerator.GenerateTasksForWaveAsync(wave, cancellationToken);
        foreach (var task in taskGenResult.GeneratedTasks)
        {
            task.PickWave = wave;
            wave.Tasks.Add(task);
        }

        if (wave.Tasks.Count == 0)
        {
            throw new DomainException($"No pick tasks could be generated for wave '{waveNumber}' from selected orders.");
        }

        // 8. Optimize Serpentine Pick Path
        var locationIds = wave.Tasks.Select(t => t.SourceLocationId).Distinct().ToList();
        var locations = await _context.Locations
            .Where(l => locationIds.Contains(l.Id))
            .Include(l => l.Locks)
            .ToDictionaryAsync(l => l.Id, l => l, cancellationToken);

        var routeResult = _routeOptimizer.OptimizeRoute(wave.Tasks.ToList(), locations);

        _logger.LogInformation(
            "Generated Wave '{WaveNumber}' ({WaveType}) with {Orders} orders, {Tasks} tasks, {Units} units. Route Stops: {Stops}, Distance: {Distance:F1}m",
            wave.WaveNumber, wave.WaveType, wave.WaveOrders.Count, wave.Tasks.Count,
            wave.Tasks.Sum(t => t.QuantityToPick), routeResult.TotalStops, routeResult.EstimatedTravelMeters);

        return new PickWaveGenerationResult
        {
            Wave = wave,
            Tasks = wave.Tasks.ToList(),
            Route = routeResult
        };
    }
}

