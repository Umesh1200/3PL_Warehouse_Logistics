using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Authoritative transactional execution service for hard inventory reservation, optimistic concurrency retry,
/// and de-allocation lifecycles.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class AllocationExecutionService : IAllocationExecutionService
{
    private readonly IApplicationDbContext _context;
    private readonly IAllocationEngine _allocationEngine;
    private readonly IAllocationCandidateFilter _candidateFilter;
    private readonly ITenantProvider _tenantProvider;

    public AllocationExecutionService(
        IApplicationDbContext context,
        IAllocationEngine allocationEngine,
        IAllocationCandidateFilter candidateFilter,
        ITenantProvider tenantProvider)
    {
        _context = context;
        _allocationEngine = allocationEngine;
        _candidateFilter = candidateFilter;
        _tenantProvider = tenantProvider;
    }

    public async Task<AllocationExecutionResultDto> ExecuteOrderAllocationAsync(
        Guid outboundOrderId,
        AllocationStrategyType? strategyOverride = null,
        string? idempotencyKey = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await _context.BeginTransactionAsync(cancellationToken);

            try
            {
                // 1. Retrieve order aggregate root with lines, items, and allocations
                var order = await _context.OutboundOrders
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Item)
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Allocations)
                    .Include(o => o.Shortages)
                    .Include(o => o.StatusHistories)
                    .FirstOrDefaultAsync(o => o.Id == outboundOrderId, cancellationToken);

                if (order == null)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    throw new EntityNotFoundException(nameof(OutboundOrder), outboundOrderId);
                }

                // 2. Tenant isolation verification
                if (!_tenantProvider.BypassTenantFilter && _tenantProvider.TenantId.HasValue)
                {
                    if (order.ClientId != _tenantProvider.TenantId.Value)
                    {
                        await _context.RollbackTransactionAsync(cancellationToken);
                        throw new UnauthorizedAccessException($"Cross-tenant allocation attempt rejected for order '{order.OrderNumber}'.");
                    }
                }

                // 3. Lifecycle state checks
                if (order.IsOnHold)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    throw new InvalidDomainStateException(
                        $"Cannot allocate order '{order.OrderNumber}' because it is currently OnHold (Reason: {order.HoldReason}).");
                }

                if (order.Status == OutboundOrderStatus.Completed || order.Status == OutboundOrderStatus.Cancelled)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    throw new InvalidDomainStateException(
                        $"Cannot allocate order '{order.OrderNumber}' with status '{order.Status}'.");
                }

                // 4. Duplicate request / idempotency protection
                if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                    string.Equals(order.AllocationIdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    return BuildSummary(order, isDuplicate: true, attempt: attempt);
                }

                var activeLines = order.Lines
                    .Where(l => !l.IsDeleted && l.Status != OutboundOrderLineStatus.Cancelled && l.RemainingDemand > 0)
                    .OrderBy(l => l.LineNumber)
                    .ToList();

                if (activeLines.Count == 0)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    return BuildSummary(order, isDuplicate: false, attempt: attempt);
                }

                // 5. Execute allocation planning & transactional reservation per line
                foreach (var line in activeLines)
                {
                    var request = new AllocationRequest
                    {
                        ClientId = order.ClientId,
                        FacilityId = order.FacilityId,
                        OutboundOrderId = order.Id,
                        OutboundOrderLineId = line.Id,
                        ItemId = line.ItemId,
                        QuantityRequested = line.RemainingDemand,
                        RequiredLotNumber = line.RequiredLotNumber,
                        MinExpiryDateUtc = line.MinExpiryDateUtc
                    };

                    var linePlan = await _allocationEngine.PlanLineAllocationAsync(request, cancellationToken);

                    foreach (var candidatePlan in linePlan.CandidateAllocations)
                    {
                        if (line.RemainingDemand <= 0) break;

                        // Revalidate candidate stock balance inside transaction
                        var stock = await _context.StockOnHand
                            .Include(s => s.Location)
                                .ThenInclude(l => l.Locks)
                            .Include(s => s.Location)
                                .ThenInclude(l => l.Zone)
                            .Include(s => s.Lot)
                            .Include(s => s.Item)
                            .FirstOrDefaultAsync(s => s.Id == candidatePlan.StockOnHandId, cancellationToken);

                        if (stock == null) continue;

                        var eligibility = _candidateFilter.Evaluate(stock, request);
                        if (!eligibility.IsEligible) continue;

                        var availableNow = stock.QtyAvailable;
                        if (availableNow <= 0) continue;

                        var qtyToReserve = Math.Min(line.RemainingDemand, Math.Min(candidatePlan.QuantityAllocated, availableNow));
                        if (qtyToReserve <= 0) continue;

                        // Atomic hard reservation mutation
                        stock.QtyAvailable -= qtyToReserve;
                        stock.QtyAllocated += qtyToReserve;
                        stock.Version++;

                        var allocation = new InventoryAllocation
                        {
                            ClientId = order.ClientId,
                            FacilityId = order.FacilityId,
                            OutboundOrderId = order.Id,
                            OutboundOrderLineId = line.Id,
                            StockOnHandId = stock.Id,
                            ItemId = line.ItemId,
                            LocationId = stock.LocationId,
                            LotId = stock.LotId,
                            LpnCode = candidatePlan.LpnCode,
                            QuantityRequested = line.QuantityOrdered,
                            QuantityAllocated = qtyToReserve,
                            QuantityReleased = 0m,
                            Status = AllocationStatus.Allocated,
                            StrategyUsed = linePlan.StrategyUsed,
                            AllocatedAtUtc = DateTime.UtcNow,
                            AllocatedByUserId = userId
                        };

                        _context.InventoryAllocations.Add(allocation);
                        line.Allocations.Add(allocation);
                        line.Allocate(qtyToReserve);
                    }

                    // Record shortage if demand could not be completely satisfied
                    if (line.RemainingDemand > 0)
                    {
                        var shortageReason = linePlan.Shortages.FirstOrDefault()?.Reason ?? ShortageReason.InsufficientStock;
                        var shortage = new AllocationShortage
                        {
                            ClientId = order.ClientId,
                            FacilityId = order.FacilityId,
                            OutboundOrderId = order.Id,
                            OutboundOrderLineId = line.Id,
                            ItemId = line.ItemId,
                            QuantityRequested = line.QuantityOrdered,
                            QuantityAllocated = line.QuantityAllocated,
                            ShortageQuantity = line.RemainingDemand,
                            Reason = shortageReason,
                            Notes = $"Allocated {line.QuantityAllocated}/{line.QuantityOrdered} units ({line.RemainingDemand} shortage)."
                        };

                        _context.AllocationShortages.Add(shortage);
                        order.Shortages.Add(shortage);
                    }
                }

                // 6. Update order state & idempotency token
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    order.AllocationIdempotencyKey = idempotencyKey;
                }

                order.RecalculateAllocationStatus(userId, "Allocated via AllocationEngine");
                order.Version++;

                // 7. Atomic transaction commit
                await _context.CommitTransactionAsync(cancellationToken);

                return BuildSummary(order, isDuplicate: false, attempt: attempt);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _context.RollbackTransactionAsync(cancellationToken);

                if (attempt >= maxAttempts)
                {
                    throw;
                }

                await Task.Delay(50 * attempt, cancellationToken);
            }
            catch
            {
                await _context.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }

        throw new InvalidOperationException($"Allocation for order '{outboundOrderId}' failed after {maxAttempts} attempts due to persistent concurrency conflicts.");
    }

    public async Task<DeallocationExecutionResultDto> ExecuteOrderDeallocationAsync(
        Guid outboundOrderId,
        string reason,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when de-allocating an order.");
        }

        await _context.BeginTransactionAsync(cancellationToken);

        try
        {
            var order = await _context.OutboundOrders
                .Include(o => o.Lines)
                .Include(o => o.Allocations)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId, cancellationToken);

            if (order == null)
            {
                await _context.RollbackTransactionAsync(cancellationToken);
                throw new EntityNotFoundException(nameof(OutboundOrder), outboundOrderId);
            }

            if (!_tenantProvider.BypassTenantFilter && _tenantProvider.TenantId.HasValue)
            {
                if (order.ClientId != _tenantProvider.TenantId.Value)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    throw new UnauthorizedAccessException($"Cross-tenant deallocation rejected for order '{order.OrderNumber}'.");
                }
            }

            var activeAllocations = order.Allocations
                .Where(a => a.ActiveAllocatedQuantity > 0 &&
                            (a.Status == AllocationStatus.Allocated || a.Status == AllocationStatus.PartiallyPicked))
                .ToList();

            decimal totalDeallocated = 0m;

            foreach (var allocation in activeAllocations)
            {
                var activeQty = allocation.ActiveAllocatedQuantity;
                if (activeQty <= 0) continue;

                var stock = await _context.StockOnHand
                    .FirstOrDefaultAsync(s => s.Id == allocation.StockOnHandId, cancellationToken);

                if (stock != null)
                {
                    stock.QtyAllocated = Math.Max(0m, stock.QtyAllocated - activeQty);
                    stock.QtyAvailable += activeQty;
                    stock.Version++;
                }

                allocation.Release(activeQty, userId ?? Guid.Empty, reason);

                var line = order.Lines.FirstOrDefault(l => l.Id == allocation.OutboundOrderLineId);
                line?.Deallocate(activeQty);

                totalDeallocated += activeQty;
            }

            order.RecalculateAllocationStatus(userId, reason);
            order.Version++;

            await _context.CommitTransactionAsync(cancellationToken);

            return new DeallocationExecutionResultDto
            {
                OutboundOrderId = order.Id,
                OrderNumber = order.OrderNumber,
                RestoredStatus = order.Status,
                TotalQuantityDeallocated = totalDeallocated,
                AllocationsReleasedCount = activeAllocations.Count,
                Reason = reason
            };
        }
        catch
        {
            await _context.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReleaseAllocationResultDto> ReleaseAllocationAsync(
        Guid allocationId,
        decimal? quantityToRelease,
        string reason,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when releasing an allocation.");
        }

        await _context.BeginTransactionAsync(cancellationToken);

        try
        {
            var allocation = await _context.InventoryAllocations
                .Include(a => a.OutboundOrder)
                    .ThenInclude(o => o.Lines)
                .Include(a => a.OutboundOrderLine)
                .FirstOrDefaultAsync(a => a.Id == allocationId, cancellationToken);

            if (allocation == null)
            {
                await _context.RollbackTransactionAsync(cancellationToken);
                throw new EntityNotFoundException(nameof(InventoryAllocation), allocationId);
            }

            var order = allocation.OutboundOrder;
            if (!_tenantProvider.BypassTenantFilter && _tenantProvider.TenantId.HasValue)
            {
                if (order.ClientId != _tenantProvider.TenantId.Value)
                {
                    await _context.RollbackTransactionAsync(cancellationToken);
                    throw new UnauthorizedAccessException($"Cross-tenant allocation release rejected.");
                }
            }

            var toRelease = quantityToRelease ?? allocation.ActiveAllocatedQuantity;
            if (toRelease <= 0)
            {
                await _context.RollbackTransactionAsync(cancellationToken);
                throw new ValidationException(nameof(quantityToRelease), "Release quantity must be greater than zero.");
            }

            if (toRelease > allocation.ActiveAllocatedQuantity)
            {
                await _context.RollbackTransactionAsync(cancellationToken);
                throw new ValidationException(nameof(quantityToRelease),
                    $"Cannot release {toRelease} units. Active allocated quantity is only {allocation.ActiveAllocatedQuantity} units.");
            }

            var stock = await _context.StockOnHand
                .FirstOrDefaultAsync(s => s.Id == allocation.StockOnHandId, cancellationToken);

            if (stock != null)
            {
                stock.QtyAllocated = Math.Max(0m, stock.QtyAllocated - toRelease);
                stock.QtyAvailable += toRelease;
                stock.Version++;
            }

            allocation.Release(toRelease, userId ?? Guid.Empty, reason);
            allocation.OutboundOrderLine.Deallocate(toRelease);

            order.RecalculateAllocationStatus(userId, reason);
            order.Version++;

            await _context.CommitTransactionAsync(cancellationToken);

            return new ReleaseAllocationResultDto
            {
                AllocationId = allocation.Id,
                OutboundOrderId = order.Id,
                LineId = allocation.OutboundOrderLineId,
                QuantityReleased = toRelease,
                ActiveRemainingQuantity = allocation.ActiveAllocatedQuantity,
                AllocationStatus = allocation.Status,
                OrderStatus = order.Status,
                LineStatus = allocation.OutboundOrderLine.Status
            };
        }
        catch
        {
            await _context.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static AllocationExecutionResultDto BuildSummary(OutboundOrder order, bool isDuplicate, int attempt)
    {
        var activeLines = order.Lines.Where(l => !l.IsDeleted && l.Status != OutboundOrderLineStatus.Cancelled).ToList();
        var totalOrdered = activeLines.Sum(l => l.QuantityOrdered);
        var totalAllocated = activeLines.Sum(l => l.QuantityAllocated);

        return new AllocationExecutionResultDto
        {
            OutboundOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            TotalQuantityOrdered = totalOrdered,
            TotalQuantityAllocated = totalAllocated,
            TotalShortageQuantity = Math.Max(0m, totalOrdered - totalAllocated),
            IsDuplicateRequest = isDuplicate,
            AttemptCount = attempt,
            LineSummaries = activeLines.Select(l => new LineAllocationSummaryDto
            {
                LineId = l.Id,
                LineNumber = l.LineNumber,
                ItemId = l.ItemId,
                Sku = l.Item?.Sku ?? string.Empty,
                QuantityOrdered = l.QuantityOrdered,
                QuantityAllocated = l.QuantityAllocated,
                RemainingDemand = l.RemainingDemand,
                Status = l.Status,
                Allocations = l.Allocations.Select(a => new AllocationItemDto
                {
                    AllocationId = a.Id,
                    StockOnHandId = a.StockOnHandId,
                    LocationId = a.LocationId,
                    LpnCode = a.LpnCode,
                    QuantityAllocated = a.QuantityAllocated,
                    Status = a.Status
                }).ToList()
            }).ToList(),
            Shortages = order.Shortages.Select(s => new AllocationShortageDto
            {
                ShortageId = s.Id,
                LineId = s.OutboundOrderLineId,
                ItemId = s.ItemId,
                ShortageQuantity = s.ShortageQuantity,
                Reason = s.Reason,
                Notes = s.Notes ?? string.Empty
            }).ToList()
        };
    }
}

