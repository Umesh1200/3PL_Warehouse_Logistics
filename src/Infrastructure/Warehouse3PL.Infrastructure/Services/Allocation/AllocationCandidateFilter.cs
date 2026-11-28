using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Allocation;

/// <summary>
/// Shared candidate eligibility component enforcing multi-tenancy, location locks, temperature integrity,
/// lot requirements, expiration horizons, and quality inspection statuses across all allocation strategies.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class AllocationCandidateFilter : IAllocationCandidateFilter
{
    public CandidateEligibilityResult Evaluate(StockOnHand stock, AllocationRequest request)
    {
        // 1. Tenant and facility ownership
        if (stock.ClientId != request.ClientId || stock.FacilityId != request.FacilityId || stock.ItemId != request.ItemId)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.InsufficientStock,
                "Tenant, facility, or item mismatch.");
        }

        // 2. Positive available quantity
        if (stock.QtyAvailable <= 0)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.InsufficientStock,
                "Zero or negative available inventory quantity.");
        }

        // 3. Location validity & pickability
        if (stock.Location == null)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.LocationLocked,
                "Storage location reference is missing or null.");
        }

        if (!stock.Location.IsActive || stock.Location.Status != LocationStatus.Active || stock.Location.IsDeleted || stock.Location.IsDamaged)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.LocationLocked,
                $"Location '{stock.Location.LocationCode}' is inactive, deleted, or damaged.");
        }

        if (!stock.Location.IsStorable)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.InsufficientStock,
                $"Location '{stock.Location.LocationCode}' is marked as non-storable and excluded from standard allocation.");
        }

        if (!stock.Location.IsPickable)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.LocationLocked,
                $"Location '{stock.Location.LocationCode}' is marked as non-pickable.");
        }

        // 4. Location locks (Cycle Count, Maintenance, Quarantine)
        if (stock.Location.IsLocked || (stock.Location.Locks != null && stock.Location.Locks.Any(l => l.IsActive)))
        {
            var activeLock = stock.Location.Locks?.FirstOrDefault(l => l.IsActive);
            var reason = activeLock?.ReasonCode ?? "Maintenance / Operational Lock";
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.LocationLocked,
                $"Location '{stock.Location.LocationCode}' is locked ({reason}).");
        }

        // 5. Exclude Transient Staging and Dock Doors
        if (stock.Location.LocationType == LocationType.StagingLane || stock.Location.LocationType == LocationType.DockDoor)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.InsufficientStock,
                $"Location '{stock.Location.LocationCode}' type '{stock.Location.LocationType}' is excluded from standard outbound allocation.");
        }

        // 6. Valid Inventory Quality Status (QA inspection clearance)
        if (stock.Lot != null && stock.Lot.QaStatus != LotQaStatus.Released)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.NoValidLotsAvailable,
                $"Lot '{stock.Lot.LotNumber}' has QA status '{stock.Lot.QaStatus}' (requires Released).");
        }

        // 7. Order-Specific Lot Restriction
        if (!string.IsNullOrWhiteSpace(request.RequiredLotNumber))
        {
            if (stock.Lot == null || !string.Equals(stock.Lot.LotNumber, request.RequiredLotNumber, StringComparison.OrdinalIgnoreCase))
            {
                return CandidateEligibilityResult.Ineligible(
                    ShortageReason.LotRestrictionUnmet,
                    $"Demand requires specific lot '{request.RequiredLotNumber}', but candidate lot is '{stock.Lot?.LotNumber ?? "None"}'.");
            }
        }

        // 8. Expiry and Shelf-Life Horizons
        if (stock.Lot != null && stock.Lot.ExpirationDate.HasValue)
        {
            var expDate = stock.Lot.ExpirationDate.Value;
            var now = DateTime.UtcNow;

            if (expDate <= now)
            {
                return CandidateEligibilityResult.Ineligible(
                    ShortageReason.ExpiredLotsOnly,
                    $"Lot '{stock.Lot.LotNumber}' expired on {expDate:yyyy-MM-dd}.");
            }

            if (request.MinExpiryDateUtc.HasValue && expDate < request.MinExpiryDateUtc.Value)
            {
                return CandidateEligibilityResult.Ineligible(
                    ShortageReason.ExpiredLotsOnly,
                    $"Lot '{stock.Lot.LotNumber}' expires on {expDate:yyyy-MM-dd}, failing requested cutoff {request.MinExpiryDateUtc.Value:yyyy-MM-dd}.");
            }

            if (request.Policy != null && request.Policy.MinDaysBeforeExpiry > 0)
            {
                var minDate = now.AddDays(request.Policy.MinDaysBeforeExpiry);
                if (expDate < minDate)
                {
                    return CandidateEligibilityResult.Ineligible(
                        ShortageReason.ExpiredLotsOnly,
                        $"Lot '{stock.Lot.LotNumber}' expires on {expDate:yyyy-MM-dd}, violating minimum policy horizon of {request.Policy.MinDaysBeforeExpiry} days.");
                }
            }
        }
        else if (stock.Item != null && stock.Item.RequiresExpiryDate)
        {
            return CandidateEligibilityResult.Ineligible(
                ShortageReason.NoValidLotsAvailable,
                $"Item '{stock.Item.Sku}' requires lot expiration date, but none is recorded.");
        }

        // 9. Temperature Integrity & Zone Compatibility
        if (stock.Item != null && stock.Location.Zone != null)
        {
            var zone = stock.Location.Zone;
            if (stock.Item.StorageTempClass == StorageTempClass.Frozen)
            {
                var isFrozen = zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= -15m);
                if (!isFrozen)
                {
                    return CandidateEligibilityResult.Ineligible(
                        ShortageReason.TempClassMismatch,
                        $"Frozen SKU '{stock.Item.Sku}' cannot be allocated from non-freezer zone '{zone.ZoneCode}'.");
                }
            }
            else if (stock.Item.StorageTempClass == StorageTempClass.Refrigerated)
            {
                var isChilled = zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= 8m);
                if (!isChilled)
                {
                    return CandidateEligibilityResult.Ineligible(
                        ShortageReason.TempClassMismatch,
                        $"Refrigerated SKU '{stock.Item.Sku}' cannot be allocated from non-refrigerated zone '{zone.ZoneCode}'.");
                }
            }
        }

        return CandidateEligibilityResult.Success();
    }
}

