using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// First Expired, First Out (FEFO) inventory allocation strategy.
/// Selects and orders candidate stock based on lot expiration date (Lot.ExpirationDate).
/// Strictly read-only; does not mutate inventory balances.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class FefoAllocationStrategy : IAllocationStrategy
{
    private readonly IApplicationDbContext _context;
    private readonly IAllocationCandidateFilter _candidateFilter;

    public AllocationStrategyType StrategyType => AllocationStrategyType.FEFO;

    public FefoAllocationStrategy(
        IApplicationDbContext context,
        IAllocationCandidateFilter candidateFilter)
    {
        _context = context;
        _candidateFilter = candidateFilter;
    }

    public async Task<IReadOnlyList<StockOnHand>> SelectCandidatesAsync(
        AllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Query candidates for the requested tenant, facility, and SKU with positive stock
        var rawCandidates = await _context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Location)
                .ThenInclude(l => l.Locks)
            .Include(s => s.Location)
                .ThenInclude(l => l.Zone)
            .Include(s => s.Lot)
            .Include(s => s.Item)
            .Where(s => s.ClientId == request.ClientId &&
                        s.FacilityId == request.FacilityId &&
                        s.ItemId == request.ItemId &&
                        s.QtyAvailable > 0)
            .ToListAsync(cancellationToken);

        // 2. Evaluate shared candidate eligibility
        var eligibleCandidates = rawCandidates
            .Where(s => _candidateFilter.Evaluate(s, request).IsEligible)
            .ToList();

        if (eligibleCandidates.Count == 0)
        {
            return Array.Empty<StockOnHand>();
        }

        // 3. Multi-tiered deterministic FEFO ordering:
        //    Tier 1: Lot.ExpirationDate ASC (fallback to DateTime.MaxValue if no lot)
        //    Tier 2: Location.PickSequence ASC
        //    Tier 3: Bin-consolidation preference (Single-bin satisfaction first, then residual cleanout QtyAvailable ASC)
        //    Tier 4: StockOnHand.Id ASC as stable final tie-breaker
        var sortedCandidates = eligibleCandidates
            .OrderBy(s => s.Lot?.ExpirationDate ?? DateTime.MaxValue)
            .ThenBy(s => s.Location?.PickSequence ?? int.MaxValue)
            .ThenBy(s => s.QtyAvailable >= request.QuantityRequested ? 0 : 1)
            .ThenBy(s => s.QtyAvailable)
            .ThenBy(s => s.Id)
            .ToList();

        return sortedCandidates;
    }
}

