using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Shared deterministic candidate ordering across FIFO, FEFO, and Manual strategies.
/// When <see cref="AllocationRequest.Policy"/> enables <c>PreferFullHandlingUnits</c>, intact handling units
/// that can be picked completely without breaking pallets/LPNs are prioritized within each SLA tier.
/// </summary>
internal static class AllocationCandidateOrdering
{
    public static IReadOnlyList<StockOnHand> OrderFifo(
        IEnumerable<StockOnHand> eligible,
        AllocationRequest request)
    {
        bool preferFullHu = request.Policy?.PreferFullHandlingUnits == true;
        return OrderWithPrimaryKey(
            eligible,
            request,
            s => s.ReceivedAtUtc ?? s.CreatedAt,
            preferFullHu);
    }

    public static IReadOnlyList<StockOnHand> OrderFefo(
        IEnumerable<StockOnHand> eligible,
        AllocationRequest request)
    {
        bool preferFullHu = request.Policy?.PreferFullHandlingUnits == true;
        return OrderWithPrimaryKey(
            eligible,
            request,
            s => s.Lot?.ExpirationDate ?? DateTime.MaxValue,
            preferFullHu);
    }

    public static IReadOnlyList<StockOnHand> OrderManual(
        IEnumerable<StockOnHand> eligible,
        AllocationRequest request)
    {
        bool preferFullHu = request.Policy?.PreferFullHandlingUnits == true;
        return OrderWithPrimaryKey(
            eligible,
            request,
            _ => 0,
            preferFullHu);
    }

    private static List<StockOnHand> OrderWithPrimaryKey<TPrimary>(
        IEnumerable<StockOnHand> eligible,
        AllocationRequest request,
        Func<StockOnHand, TPrimary> primaryKeySelector,
        bool preferFullHu)
    {
        if (!preferFullHu)
        {
            return eligible
                .OrderBy(primaryKeySelector)
                .ThenBy(s => s.Location?.PickSequence ?? int.MaxValue)
                .ThenBy(s => s.QtyAvailable >= request.QuantityRequested ? 0 : 1)
                .ThenBy(s => s.QtyAvailable)
                .ThenBy(s => s.Id)
                .ToList();
        }

        // Within each primary SLA tier, select candidates dynamically against remaining demand so intact HUs
        // are prioritized first while avoiding unnecessary partial HU splits when loose stock can satisfy the remainder.
        var ordered = new List<StockOnHand>();
        var remainingDemand = Math.Max(0m, request.QuantityRequested);

        foreach (var tier in eligible.OrderBy(primaryKeySelector).GroupBy(primaryKeySelector))
        {
            var pool = tier.ToList();
            while (pool.Count > 0)
            {
                if (remainingDemand <= 0m)
                {
                    ordered.AddRange(pool
                        .OrderBy(s => s.Location?.PickSequence ?? int.MaxValue)
                        .ThenBy(s => s.QtyAvailable)
                        .ThenBy(s => s.Id));
                    break;
                }

                var next = pool
                    .OrderBy(s => GetResidualHandlingUnitRank(s, remainingDemand))
                    .ThenByDescending(s => IsIntactFullHandlingUnit(s, remainingDemand) ? s.QtyAvailable : 0m)
                    .ThenBy(s => s.Location?.PickSequence ?? int.MaxValue)
                    .ThenBy(s => s.QtyAvailable >= remainingDemand ? 0 : 1)
                    .ThenBy(s => s.QtyAvailable)
                    .ThenBy(s => s.Id)
                    .First();

                ordered.Add(next);
                pool.Remove(next);
                remainingDemand = Math.Max(0m, remainingDemand - next.QtyAvailable);
            }
        }

        return ordered;
    }

    private static bool HasHandlingUnit(StockOnHand stock) =>
        stock.HandlingUnitId.HasValue || !string.IsNullOrWhiteSpace(stock.LpnCode);

    private static bool IsIntactFullHandlingUnit(StockOnHand stock, decimal remainingDemand) =>
        HasHandlingUnit(stock) && stock.QtyAvailable > 0m && stock.QtyAvailable <= remainingDemand;

    private static int GetResidualHandlingUnitRank(StockOnHand stock, decimal remainingDemand)
    {
        if (!HasHandlingUnit(stock))
        {
            return 1; // Loose stock: preferred over breaking an oversized handling unit for residual demand
        }

        return stock.QtyAvailable <= remainingDemand
            ? 0 // Full intact handling unit that fits within remaining demand without breaking container
            : 2; // Oversized handling unit requiring a partial pallet/LPN break
    }
}
