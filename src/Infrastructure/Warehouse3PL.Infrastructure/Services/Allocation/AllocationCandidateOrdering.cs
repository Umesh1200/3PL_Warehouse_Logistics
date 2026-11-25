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

        // Full handling units that fit completely within requested demand (intact LPN/HU picks) come first,
        // largest intact HU first to minimize touches, followed by partial HUs, then loose bin stock.
        return eligible
            .OrderBy(primaryKeySelector)
            .ThenBy(s => GetHandlingUnitRank(s, request.QuantityRequested))
            .ThenByDescending(s => IsIntactFullHandlingUnit(s, request.QuantityRequested) ? s.QtyAvailable : 0m)
            .ThenBy(s => s.Location?.PickSequence ?? int.MaxValue)
            .ThenBy(s => s.QtyAvailable >= request.QuantityRequested ? 0 : 1)
            .ThenBy(s => s.QtyAvailable)
            .ThenBy(s => s.Id)
            .ToList();
    }

    private static bool HasHandlingUnit(StockOnHand stock) =>
        stock.HandlingUnitId.HasValue || !string.IsNullOrWhiteSpace(stock.LpnCode);

    private static bool IsIntactFullHandlingUnit(StockOnHand stock, decimal requestedQty) =>
        HasHandlingUnit(stock) && stock.QtyAvailable > 0m && stock.QtyAvailable <= requestedQty;

    private static int GetHandlingUnitRank(StockOnHand stock, decimal requestedQty)
    {
        if (!HasHandlingUnit(stock))
        {
            return 2; // Loose bin stock
        }

        return stock.QtyAvailable <= requestedQty
            ? 0 // Full intact handling unit that avoids breaking container
            : 1; // Handling unit that exceeds remaining demand (requires partial break)
    }
}
