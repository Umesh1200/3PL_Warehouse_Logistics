using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Putaway;

/// <summary>
/// Enforces shared putaway strategy precedence, destination temperature compatibility,
/// and physical weight/volume capacity checks across slotting recommendation and confirmation.
/// </summary>
public static class PutawaySafetyValidator
{
    public static decimal ResolveItemVolumeCm3(Item item) =>
        item.VolumeCm3 > 0m
            ? item.VolumeCm3
            : item.LengthCm * item.WidthCm * item.HeightCm;

    public static async Task<PutawayStrategyRule?> ResolveEffectiveRuleAsync(
        IApplicationDbContext context,
        Guid clientId,
        Guid facilityId,
        Item item,
        CancellationToken cancellationToken)
    {
        var strategyRules = await context.PutawayStrategyRules
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId &&
                        (r.ClientId == clientId || r.ClientId == Guid.Empty) &&
                        r.IsActive &&
                        !r.IsDeleted)
            .OrderBy(r => r.Sequence)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return ResolveEffectiveRule(strategyRules, clientId, item);
    }

    public static PutawayStrategyRule? ResolveEffectiveRule(
        IEnumerable<PutawayStrategyRule> rules,
        Guid clientId,
        Item item)
    {
        return rules
            .Where(r => r.TempClassRequirement == item.StorageTempClass &&
                        (!r.TargetVelocity.HasValue || (int)r.TargetVelocity.Value == (int)item.AbcVelocity))
            .OrderBy(r => r.ClientId == clientId ? 0 : 1)
            .ThenBy(r => r.TargetVelocity.HasValue ? 0 : 1)
            .ThenBy(r => r.Sequence)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    public static bool IsZoneTemperatureCompatible(
        WarehouseZone? zone,
        Item item,
        Location destination,
        bool allowsAmbientDynamicOverflow,
        out string? failureReason)
    {
        if (item.StorageTempClass == StorageTempClass.Frozen)
        {
            var isValidFreezer = zone != null &&
                zone.TempMaxC.HasValue &&
                zone.TempMaxC.Value <= -18m &&
                (!zone.TempMinC.HasValue || zone.TempMinC.Value <= zone.TempMaxC.Value);

            if (!isValidFreezer)
            {
                failureReason = $"Frozen SKU '{item.Sku}' (-18°C or below) cannot be put away into location '{destination.LocationCode}' (zone temp max: {zone?.TempMaxC?.ToString() ?? "unspecified"}°C).";
                return false;
            }

            failureReason = null;
            return true;
        }

        if (item.StorageTempClass == StorageTempClass.Refrigerated)
        {
            var isValidChilled = zone != null &&
                zone.TempMaxC.HasValue &&
                zone.TempMaxC.Value >= 2m &&
                zone.TempMaxC.Value <= 8m &&
                (!zone.TempMinC.HasValue || (zone.TempMinC.Value >= 2m && zone.TempMinC.Value <= zone.TempMaxC.Value));

            if (!isValidChilled)
            {
                failureReason = $"Refrigerated SKU '{item.Sku}' (2°C to 8°C) cannot be put away into location '{destination.LocationCode}' (zone range: {zone?.TempMinC?.ToString() ?? "unspecified"}°C to {zone?.TempMaxC?.ToString() ?? "unspecified"}°C).";
                return false;
            }

            failureReason = null;
            return true;
        }

        if (item.StorageTempClass == StorageTempClass.TemperatureControlled)
        {
            var isValidControlled = zone != null &&
                zone.TempMinC.HasValue &&
                zone.TempMaxC.HasValue &&
                zone.TempMinC.Value >= 15m &&
                zone.TempMaxC.Value <= 25m &&
                zone.TempMinC.Value <= zone.TempMaxC.Value;

            if (!isValidControlled)
            {
                failureReason = $"Temperature-controlled SKU '{item.Sku}' (15°C to 25°C) cannot be put away into location '{destination.LocationCode}' (zone range: {zone?.TempMinC?.ToString() ?? "unspecified"}°C to {zone?.TempMaxC?.ToString() ?? "unspecified"}°C).";
                return false;
            }

            failureReason = null;
            return true;
        }

        var isColdOrChilledZone = zone != null &&
            (zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= 8m));
        if (isColdOrChilledZone && !allowsAmbientDynamicOverflow)
        {
            failureReason = $"Ambient SKU '{item.Sku}' cannot be placed into cold storage location '{destination.LocationCode}' unless dynamic overflow is permitted by the effective putaway strategy.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static async Task ValidateDestinationSafetyAndCapacityAsync(
        IApplicationDbContext context,
        Location destination,
        Item item,
        decimal incomingQuantity,
        CancellationToken cancellationToken,
        Guid? lotId = null)
    {
        if (destination.IsDeleted || !destination.IsActive || destination.Status != LocationStatus.Active)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is not active (status: {destination.Status}).");
        }

        if (!destination.IsStorable)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is not marked as storable.");
        }

        if (destination.IsLocked || destination.IsDamaged)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is locked or damaged and cannot accept inventory.");
        }

        if (destination.Zone?.DedicatedClientId is Guid zoneClientId && zoneClientId != item.ClientId)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination zone '{destination.Zone.ZoneCode}' is dedicated to another client and cannot accept inventory for client '{item.ClientId}'.");
        }

        var storageProfile = destination.StorageProfile ?? await context.LocationStorageProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == destination.Id && p.FacilityId == destination.FacilityId, cancellationToken);

        if (storageProfile != null)
        {
            if (!storageProfile.IsPutawayAllowed)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Putaway is disallowed by storage profile on location '{destination.LocationCode}'.");
            }

            if (storageProfile.DedicatedClientId.HasValue && storageProfile.DedicatedClientId.Value != item.ClientId)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' is dedicated to another client.");
            }
        }

        var effectiveRule = await ResolveEffectiveRuleAsync(context, item.ClientId, destination.FacilityId, item, cancellationToken);
        var allowsDynamicOverflow = effectiveRule?.AllowDynamicOverflow == true;

        if (!IsZoneTemperatureCompatible(destination.Zone, item, destination, allowsDynamicOverflow, out var failureReason))
        {
            throw new ValidationException(nameof(destination), failureReason!);
        }

        bool needsStockCheck = destination.MaxWeightKg.HasValue ||
                               destination.MaxVolumeCm3.HasValue ||
                               (storageProfile != null && (!storageProfile.AllowMixedSku || !storageProfile.AllowMixedLot));

        if (needsStockCheck)
        {
            var existingStocks = await context.StockOnHand
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Where(s => s.FacilityId == destination.FacilityId && s.LocationId == destination.Id)
                .ToListAsync(cancellationToken);

            if (storageProfile != null && !storageProfile.AllowMixedSku &&
                existingStocks.Any(s => s.TotalQuantity > 0m && s.ItemId != item.Id))
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' does not allow mixed SKUs.");
            }

            if (storageProfile != null && !storageProfile.AllowMixedLot &&
                existingStocks.Any(s => s.TotalQuantity > 0m && s.LotId != lotId))
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' does not allow mixed lots.");
            }

            var currentWeight = existingStocks.Sum(s => s.TotalQuantity * s.Item.WeightKg);
            var incomingWeight = incomingQuantity * item.WeightKg;
            if (destination.MaxWeightKg.HasValue && (currentWeight + incomingWeight) > destination.MaxWeightKg.Value)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' weight limit ({destination.MaxWeightKg.Value} kg) would be exceeded (current: {currentWeight} kg, incoming: {incomingWeight} kg).");
            }

            var currentVolume = existingStocks.Sum(s => s.TotalQuantity * ResolveItemVolumeCm3(s.Item));
            var incomingVolume = incomingQuantity * ResolveItemVolumeCm3(item);
            if (destination.MaxVolumeCm3.HasValue && (currentVolume + incomingVolume) > destination.MaxVolumeCm3.Value)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' volume limit ({destination.MaxVolumeCm3.Value} cm3) would be exceeded.");
            }
        }

        if (storageProfile?.MaxHandlingUnits is int maxHus)
        {
            var activeHuCount = await context.HandlingUnits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(hu => hu.FacilityId == destination.FacilityId &&
                                  hu.CurrentLocationId == destination.Id &&
                                  !hu.IsDeleted,
                    cancellationToken);

            if (activeHuCount >= maxHus)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' handling-unit limit ({maxHus}) has been reached.");
            }
        }
    }

    /// <summary>
    /// Validates the cumulative projected inventory state of a multi-line incoming batch targeting a single destination
    /// (MDM21-F02-R01). Accounts for existing stock PLUS all incoming lines in the batch for weight, volume,
    /// mixed-SKU, mixed-lot, dedicated-client, temperature compatibility, and handling-unit capacity.
    /// </summary>
    public static async Task ValidateProjectedBatchSafetyAndCapacityAsync(
        IApplicationDbContext context,
        Location destination,
        Guid clientId,
        IReadOnlyList<(Item Item, decimal Quantity, Guid? LotId)> incomingLines,
        CancellationToken cancellationToken,
        int incomingHandlingUnitCount = 0)
    {
        if (incomingLines == null || incomingLines.Count == 0)
        {
            return;
        }

        if (destination.IsDeleted || !destination.IsActive || destination.Status != LocationStatus.Active)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is not active (status: {destination.Status}).");
        }

        if (!destination.IsStorable)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is not marked as storable.");
        }

        if (destination.IsLocked || destination.IsDamaged)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination location '{destination.LocationCode}' is locked or damaged and cannot accept inventory.");
        }

        if (destination.Zone?.DedicatedClientId is Guid zoneClientId && zoneClientId != clientId)
        {
            throw new ValidationException(
                nameof(destination),
                $"Destination zone '{destination.Zone.ZoneCode}' is dedicated to another client and cannot accept inventory for client '{clientId}'.");
        }

        var storageProfile = destination.StorageProfile ?? await context.LocationStorageProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == destination.Id && p.FacilityId == destination.FacilityId, cancellationToken);

        if (storageProfile != null)
        {
            if (!storageProfile.IsPutawayAllowed)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Putaway is disallowed by storage profile on location '{destination.LocationCode}'.");
            }

            if (storageProfile.DedicatedClientId.HasValue && storageProfile.DedicatedClientId.Value != clientId)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' is dedicated to another client.");
            }
        }

        foreach (var (item, _, _) in incomingLines)
        {
            if (item.ClientId != clientId)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Item '{item.Sku}' does not belong to client '{clientId}'.");
            }

            var effectiveRule = await ResolveEffectiveRuleAsync(context, clientId, destination.FacilityId, item, cancellationToken);
            var allowsDynamicOverflow = effectiveRule?.AllowDynamicOverflow == true;

            if (!IsZoneTemperatureCompatible(destination.Zone, item, destination, allowsDynamicOverflow, out var failureReason))
            {
                throw new ValidationException(nameof(destination), failureReason!);
            }
        }

        bool needsStockCheck = destination.MaxWeightKg.HasValue ||
                               destination.MaxVolumeCm3.HasValue ||
                               (storageProfile != null && (!storageProfile.AllowMixedSku || !storageProfile.AllowMixedLot));

        if (needsStockCheck)
        {
            var existingStocks = await context.StockOnHand
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Item)
                .Where(s => s.FacilityId == destination.FacilityId && s.LocationId == destination.Id)
                .ToListAsync(cancellationToken);

            var activeExisting = existingStocks.Where(s => s.TotalQuantity > 0m).ToList();
            var positiveIncoming = incomingLines.Where(l => l.Quantity > 0m).ToList();

            if (storageProfile != null && !storageProfile.AllowMixedSku)
            {
                var projectedSkus = activeExisting.Select(s => s.ItemId)
                    .Concat(positiveIncoming.Select(l => l.Item.Id))
                    .Distinct()
                    .Count();
                if (projectedSkus > 1)
                {
                    throw new ValidationException(
                        nameof(destination),
                        $"Destination location '{destination.LocationCode}' does not allow mixed SKUs (projected distinct SKUs: {projectedSkus}).");
                }
            }

            if (storageProfile != null && !storageProfile.AllowMixedLot)
            {
                var projectedLots = activeExisting.Select(s => s.LotId)
                    .Concat(positiveIncoming.Select(l => l.LotId))
                    .Distinct()
                    .Count();
                if (projectedLots > 1)
                {
                    throw new ValidationException(
                        nameof(destination),
                        $"Destination location '{destination.LocationCode}' does not allow mixed lots (projected distinct lots: {projectedLots}).");
                }
            }

            var currentWeight = activeExisting.Sum(s => s.TotalQuantity * s.Item.WeightKg);
            var incomingWeight = positiveIncoming.Sum(l => l.Quantity * l.Item.WeightKg);
            if (destination.MaxWeightKg.HasValue && (currentWeight + incomingWeight) > destination.MaxWeightKg.Value)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' weight limit ({destination.MaxWeightKg.Value} kg) would be exceeded by cumulative batch (current: {currentWeight} kg, incoming batch: {incomingWeight} kg).");
            }

            var currentVolume = activeExisting.Sum(s => s.TotalQuantity * ResolveItemVolumeCm3(s.Item));
            var incomingVolume = positiveIncoming.Sum(l => l.Quantity * ResolveItemVolumeCm3(l.Item));
            if (destination.MaxVolumeCm3.HasValue && (currentVolume + incomingVolume) > destination.MaxVolumeCm3.Value)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' volume limit ({destination.MaxVolumeCm3.Value} cm3) would be exceeded by cumulative batch (current: {currentVolume} cm3, incoming batch: {incomingVolume} cm3).");
            }
        }

        if (storageProfile?.MaxHandlingUnits is int maxHusLimit)
        {
            var activeHuCount = await context.HandlingUnits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(hu => hu.FacilityId == destination.FacilityId &&
                                  hu.CurrentLocationId == destination.Id &&
                                  !hu.IsDeleted,
                    cancellationToken);

            var projectedHus = activeHuCount + Math.Max(0, incomingHandlingUnitCount);
            if (projectedHus > maxHusLimit || (incomingHandlingUnitCount == 0 && activeHuCount > maxHusLimit))
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Destination location '{destination.LocationCode}' handling-unit limit ({maxHusLimit}) would be exceeded (current: {activeHuCount}, incoming: {incomingHandlingUnitCount}).");
            }
        }
    }
}
