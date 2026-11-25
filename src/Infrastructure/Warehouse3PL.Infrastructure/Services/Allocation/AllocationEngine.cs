using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Core coordination engine for outbound inventory allocation planning.
/// Coordinates policy lookup, strategy resolution, multi-bin candidate allocation, and shortage detection.
/// Strictly read-only in Step 2; physical reservations and mutations are executed in Step 3.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class AllocationEngine : IAllocationEngine
{
    private readonly IApplicationDbContext _context;
    private readonly IAllocationStrategyResolver _strategyResolver;
    private readonly IAllocationCandidateFilter _candidateFilter;

    public AllocationEngine(
        IApplicationDbContext context,
        IAllocationStrategyResolver strategyResolver,
        IAllocationCandidateFilter candidateFilter)
    {
        _context = context;
        _strategyResolver = strategyResolver;
        _candidateFilter = candidateFilter;
    }

    public async Task<LineAllocationPlan> PlanLineAllocationAsync(
        AllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StrategyOverride.HasValue && !Enum.IsDefined(typeof(AllocationStrategyType), request.StrategyOverride.Value))
        {
            throw new ValidationException(nameof(request.StrategyOverride), $"Invalid allocation strategy override '{request.StrategyOverride.Value}'.");
        }

        // 1. Resolve Policy if not provided
        var policy = request.Policy ?? await ResolvePolicyAsync(request.ClientId, request.FacilityId, request.ItemId, cancellationToken);
        request.Policy = policy;

        // 2. Determine strategy (Manual if designation criteria set, otherwise explicit validated override, otherwise policy default)
        var strategyType = request.PreferredLocationId.HasValue || request.PreferredLotId.HasValue
            ? AllocationStrategyType.Manual
            : (request.StrategyOverride ?? policy.DefaultStrategy);

        var strategy = _strategyResolver.GetStrategy(strategyType);

        // 3. Query SKU information for reporting
        var item = await _context.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken);

        var plan = new LineAllocationPlan
        {
            OutboundOrderLineId = request.OutboundOrderLineId,
            ItemId = request.ItemId,
            Sku = item?.Sku ?? string.Empty,
            QuantityRequested = request.QuantityRequested,
            StrategyUsed = strategyType
        };

        // 4. Select ordered eligible candidates via strategy
        var candidates = await strategy.SelectCandidatesAsync(request, cancellationToken);

        // 5. Multi-bin candidate quantity evaluation
        var remainingDemand = request.QuantityRequested;
        var plannedAllocations = new List<CandidateAllocationPlan>();

        foreach (var candidate in candidates)
        {
            if (remainingDemand <= 0) break;

            var available = candidate.QtyAvailable;
            var toAllocate = Math.Min(remainingDemand, available);

            plannedAllocations.Add(new CandidateAllocationPlan
            {
                StockOnHandId = candidate.Id,
                LocationId = candidate.LocationId,
                LocationCode = candidate.Location?.LocationCode ?? string.Empty,
                LotId = candidate.LotId,
                LotNumber = candidate.Lot?.LotNumber,
                LpnCode = candidate.LpnCode ?? candidate.HandlingUnit?.LpnCode,
                QuantityAllocated = toAllocate,
                AvailableInBinBefore = available,
                AvailableInBinRemaining = available - toAllocate,
                PickSequence = candidate.Location?.PickSequence,
                ExpirationDate = candidate.Lot?.ExpirationDate,
                ReceivedAtUtc = candidate.ReceivedAtUtc ?? candidate.CreatedAt
            });

            remainingDemand -= toAllocate;
        }

        // 6. Shortage & Policy Validation
        if (remainingDemand > 0)
        {
            if (!policy.AllowPartialAllocation && plannedAllocations.Count > 0)
            {
                // Partial allocation forbidden by policy: abort line plan and record complete shortage
                plan.CandidateAllocations.Clear();
                plan.QuantityPlanned = 0m;
                plan.Shortages.Add(new AllocationShortagePlan
                {
                    OutboundOrderLineId = request.OutboundOrderLineId,
                    ItemId = request.ItemId,
                    ShortageQuantity = request.QuantityRequested,
                    Reason = ShortageReason.InsufficientStock,
                    Details = "Partial allocation disallowed by client allocation policy."
                });
            }
            else
            {
                plan.CandidateAllocations.AddRange(plannedAllocations);
                plan.QuantityPlanned = request.QuantityRequested - remainingDemand;

                // Identify specific root cause if no stock could be allocated at all
                var shortageReason = ShortageReason.InsufficientStock;
                var details = $"Unfulfilled demand shortage of {remainingDemand} units.";

                if (plannedAllocations.Count == 0)
                {
                    shortageReason = await DiagnoseShortageReasonAsync(request, cancellationToken);
                }

                plan.Shortages.Add(new AllocationShortagePlan
                {
                    OutboundOrderLineId = request.OutboundOrderLineId,
                    ItemId = request.ItemId,
                    ShortageQuantity = remainingDemand,
                    Reason = shortageReason,
                    Details = details
                });
            }
        }
        else
        {
            plan.CandidateAllocations.AddRange(plannedAllocations);
            plan.QuantityPlanned = request.QuantityRequested;
        }

        return plan;
    }

    public async Task<OrderAllocationPlan> PlanOrderAllocationAsync(
        Guid outboundOrderId,
        AllocationStrategyType? strategyOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (strategyOverride.HasValue && !Enum.IsDefined(typeof(AllocationStrategyType), strategyOverride.Value))
        {
            throw new ValidationException(nameof(strategyOverride), $"Invalid allocation strategy override '{strategyOverride.Value}'.");
        }

        var order = await _context.OutboundOrders
            .AsNoTracking()
            .Include(o => o.Lines)
                .ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(o => o.Id == outboundOrderId, cancellationToken);

        if (order == null)
        {
            throw new EntityNotFoundException(nameof(OutboundOrder), outboundOrderId);
        }

        if (order.IsOnHold)
        {
            throw new InvalidDomainStateException(
                $"Cannot plan allocation for order '{order.OrderNumber}' because it is currently OnHold (Reason: {order.HoldReason}).");
        }

        if (order.Status == OutboundOrderStatus.Completed || order.Status == OutboundOrderStatus.Cancelled)
        {
            throw new InvalidDomainStateException(
                $"Cannot plan allocation for order '{order.OrderNumber}' with status '{order.Status}'.");
        }

        var orderPlan = new OrderAllocationPlan
        {
            OutboundOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            ClientId = order.ClientId,
            FacilityId = order.FacilityId
        };

        var activeLines = order.Lines
            .Where(l => !l.IsDeleted && l.Status != OutboundOrderLineStatus.Cancelled && l.RemainingDemand > 0)
            .OrderBy(l => l.LineNumber)
            .ToList();

        foreach (var line in activeLines)
        {
            var policy = await ResolvePolicyAsync(order.ClientId, order.FacilityId, line.ItemId, cancellationToken);

            var request = new AllocationRequest
            {
                ClientId = order.ClientId,
                FacilityId = order.FacilityId,
                OutboundOrderId = order.Id,
                OutboundOrderLineId = line.Id,
                ItemId = line.ItemId,
                QuantityRequested = line.RemainingDemand,
                RequiredLotNumber = line.RequiredLotNumber,
                MinExpiryDateUtc = line.MinExpiryDateUtc,
                Policy = policy,
                StrategyOverride = strategyOverride
            };

            var linePlan = await PlanLineAllocationAsync(request, cancellationToken);
            linePlan.LineNumber = line.LineNumber;
            orderPlan.LinePlans.Add(linePlan);
        }

        orderPlan.TotalQuantityRequested = orderPlan.LinePlans.Sum(l => l.QuantityRequested);
        orderPlan.TotalQuantityPlanned = orderPlan.LinePlans.Sum(l => l.QuantityPlanned);
        orderPlan.TotalShortageQuantity = orderPlan.LinePlans.Sum(l => l.ShortageQuantity);

        return orderPlan;
    }

    private async Task<AllocationPolicy> ResolvePolicyAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        // Check category mapping for category-specific policy
        var categoryMapping = await _context.ItemCategories
            .AsNoTracking()
            .Include(c => c.CategoryMappings)
            .Where(c => c.CategoryMappings.Any(m => m.ItemId == itemId))
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (categoryMapping.HasValue)
        {
            var categoryPolicy = await _context.AllocationPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ClientId == clientId &&
                                          p.FacilityId == facilityId &&
                                          p.ItemCategoryId == categoryMapping.Value &&
                                          p.IsActive, cancellationToken);

            if (categoryPolicy != null) return categoryPolicy;
        }

        // Fallback to facility default policy for this client
        var defaultPolicy = await _context.AllocationPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ClientId == clientId &&
                                      p.FacilityId == facilityId &&
                                      p.ItemCategoryId == null &&
                                      p.IsActive, cancellationToken);

        return defaultPolicy ?? new AllocationPolicy
        {
            ClientId = clientId,
            FacilityId = facilityId,
            DefaultStrategy = AllocationStrategyType.FIFO,
            AllowPartialAllocation = true,
            AllowMixedLots = true,
            MinDaysBeforeExpiry = 0,
            IsActive = true
        };
    }

    private async Task<ShortageReason> DiagnoseShortageReasonAsync(
        AllocationRequest request,
        CancellationToken cancellationToken)
    {
        // Query raw stock to determine why none was eligible
        var rawStock = await _context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Location)
                .ThenInclude(l => l.Locks)
            .Include(s => s.Location)
                .ThenInclude(l => l.Zone)
            .Include(s => s.Lot)
            .Include(s => s.Item)
            .Where(s => s.ClientId == request.ClientId &&
                        s.FacilityId == request.FacilityId &&
                        s.ItemId == request.ItemId)
            .ToListAsync(cancellationToken);

        if (rawStock.Count == 0 || rawStock.All(s => s.QtyAvailable <= 0))
        {
            return ShortageReason.InsufficientStock;
        }

        // Inspect failure reasons
        foreach (var stock in rawStock)
        {
            var result = _candidateFilter.Evaluate(stock, request);
            if (!result.IsEligible && result.IneligibilityReason.HasValue)
            {
                return result.IneligibilityReason.Value;
            }
        }

        return ShortageReason.InsufficientStock;
    }
}

