using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Industrial 2D geometric occupancy and rectangle-packing engine for storage locations.
/// Enforces non-overlapping physical coordinate placement on the warehouse location floor,
/// orientation permutations (0 and 90 degrees), vertical stacking clearances, and boundary limits.
/// </summary>
public static class GeometricOccupancyEngine
{
    /// <summary>
    /// Attempts to place required floor stacks for an item into unoccupied 2D space of a location,
    /// avoiding collisions with existing physical placements and prior virtual proposals.
    /// Returns true if a valid, non-overlapping placement is found; otherwise false.
    /// </summary>
    public static bool TryPackItem(
        decimal locL,
        decimal locW,
        decimal locH,
        IReadOnlyList<VirtualPlacement> existingPlacements,
        Item item,
        decimal quantity,
        out List<VirtualPlacement> resultingPlacements)
    {
        resultingPlacements = [];

        if (quantity <= 0) return true;

        decimal itemL = item.LengthCm > 0 ? item.LengthCm : 30m;
        decimal itemW = item.WidthCm > 0 ? item.WidthCm : 20m;
        decimal itemH = item.HeightCm > 0 ? item.HeightCm : 20m;

        // Strict physical vertical clearance check: item height cannot exceed location vertical clearance
        if (itemH > locH)
        {
            return false;
        }

        // Strict planar boundary check: item must fit inside location in at least one permitted orientation
        bool fitsNormal = itemL <= locL && itemW <= locW;
        bool fitsRotated = item.AllowPlanarRotation && itemW <= locL && itemL <= locW;
        if (!fitsNormal && !fitsRotated)
        {
            return false;
        }

        // Vertical tiers: must be strictly > 0 without fallback to 1 when zero
        int stackFactor = Math.Max(1, item.StackabilityFactor);
        int verticalClearanceTiers = (int)(locH / itemH);
        if (verticalClearanceTiers <= 0)
        {
            return false;
        }
        int maxVerticalTiers = stackFactor <= 1 ? 1 : Math.Min(stackFactor, verticalClearanceTiers);
        if (maxVerticalTiers <= 0)
        {
            return false;
        }

        int nStacks = (int)Math.Ceiling(quantity / (decimal)maxVerticalTiers);
        if (nStacks <= 0) return true;

        // 1. Try Normal orientation first (if it fits location bounds)
        if (fitsNormal)
        {
            if (TryPackWithOrientation(locL, locW, existingPlacements, item, quantity, nStacks, maxVerticalTiers, PlanarOrientation.Normal, itemL, itemW, itemH, out var placementsNormal))
            {
                resultingPlacements = placementsNormal;
                return true;
            }
        }

        // 2. Try Rotated orientation (if rotation allowed and fits location bounds)
        if (fitsRotated)
        {
            if (TryPackWithOrientation(locL, locW, existingPlacements, item, quantity, nStacks, maxVerticalTiers, PlanarOrientation.Rotated, itemW, itemL, itemH, out var placementsRotated))
            {
                resultingPlacements = placementsRotated;
                return true;
            }
        }

        // Neither orientation could physically pack all stacks without overlapping
        return false;
    }

    private static bool TryPackWithOrientation(
        decimal locL,
        decimal locW,
        IReadOnlyList<VirtualPlacement> existingPlacements,
        Item item,
        decimal totalQty,
        int nStacks,
        int maxVerticalTiers,
        PlanarOrientation orientation,
        decimal footL,
        decimal footW,
        decimal itemH,
        out List<VirtualPlacement> placements)
    {
        placements = [];
        var allPlacements = new List<VirtualPlacement>(existingPlacements);
        decimal remainingQty = totalQty;

        for (int stackIdx = 0; stackIdx < nStacks; stackIdx++)
        {
            // Generate candidate placement coordinates from existing boundaries
            var candidateXs = new SortedSet<decimal> { 0m };
            var candidateYs = new SortedSet<decimal> { 0m };

            foreach (var p in allPlacements)
            {
                if (p.XCoordCm >= 0m && p.XCoordCm + footL <= locL + 0.0001m) candidateXs.Add(p.XCoordCm);
                if (p.YCoordCm >= 0m && p.YCoordCm + footW <= locW + 0.0001m) candidateYs.Add(p.YCoordCm);

                decimal right = p.XCoordCm + p.FootprintLengthCm;
                decimal top = p.YCoordCm + p.FootprintWidthCm;

                if (right >= 0m && right + footL <= locL + 0.0001m) candidateXs.Add(right);
                if (top >= 0m && top + footW <= locW + 0.0001m) candidateYs.Add(top);
            }

            // Order candidate points: Bottom-Left rule (Y ascending, then X ascending)
            bool stackPlaced = false;
            foreach (var y in candidateYs)
            {
                if (y + footW > locW + 0.0001m) continue;

                foreach (var x in candidateXs)
                {
                    if (x + footL > locL + 0.0001m) continue;

                    // AABB collision test against all already placed stacks
                    bool collision = false;
                    foreach (var p in allPlacements)
                    {
                        if (p.Overlaps(x, y, footL, footW))
                        {
                            collision = true;
                            break;
                        }
                    }

                    if (!collision)
                    {
                        // Found valid collision-free coordinate
                        decimal stackUnits = Math.Min(remainingQty, maxVerticalTiers);
                        var newPlacement = new VirtualPlacement
                        {
                            ItemId = item.Id,
                            Sku = item.Sku,
                            Quantity = stackUnits,
                            LengthCm = item.LengthCm,
                            WidthCm = item.WidthCm,
                            HeightCm = itemH,
                            XCoordCm = x,
                            YCoordCm = y,
                            Orientation = orientation,
                            VerticalTierCount = (int)Math.Ceiling(stackUnits),
                            StackabilityFactor = item.StackabilityFactor,
                            IsExistingPhysicalStock = false
                        };

                        allPlacements.Add(newPlacement);
                        placements.Add(newPlacement);
                        remainingQty -= stackUnits;
                        stackPlaced = true;
                        break;
                    }
                }

                if (stackPlaced) break;
            }

            if (!stackPlaced)
            {
                // Could not place this stack in the specified orientation
                placements.Clear();
                return false;
            }
        }

        return true;
    }
}
