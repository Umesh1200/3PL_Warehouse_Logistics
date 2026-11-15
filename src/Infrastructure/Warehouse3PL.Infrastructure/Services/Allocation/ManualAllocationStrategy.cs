using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Manual / Operator-designated inventory allocation strategy.
/// Selects inventory based on explicit location, lot, or container preferences while still
/// strictly enforcing quality, security, and tenant eligibility constraints.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class ManualAllocationStrategy : IAllocationStrategy
{
    private readonly IApplicationDbContext _context;
    private readonly IAllocationCandidateFilter _candidateFilter;

    public AllocationStrategyType StrategyType => AllocationStrategyType.Manual;

    public ManualAllocationStrategy(
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
        var query = _context.StockOnHand
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
                        s.QtyAvailable > 0);

        if (request.PreferredLocationId.HasValue)
        {
            query = query.Where(s => s.LocationId == request.PreferredLocationId.Value);
        }

        if (request.PreferredLotId.HasValue)
        {
            query = query.Where(s => s.LotId == request.PreferredLotId.Value);
        }

        var rawCandidates = await query.ToListAsync(cancellationToken);

        var eligibleCandidates = rawCandidates
            .Where(s => _candidateFilter.Evaluate(s, request).IsEligible)
            .ToList();

        if (eligibleCandidates.Count == 0)
        {
            return Array.Empty<StockOnHand>();
        }

        var sorted = eligibleCandidates
            .OrderBy(s => s.Location?.PickSequence ?? int.MaxValue)
            .ThenBy(s => s.QtyAvailable >= request.QuantityRequested ? 0 : 1)
            .ThenBy(s => s.QtyAvailable)
            .ThenBy(s => s.Id)
            .ToList();

        return sorted;
    }
}

