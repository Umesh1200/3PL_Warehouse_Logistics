using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Putaway;

/// <summary>
/// Enforces shared destination temperature compatibility and physical weight/volume capacity checks
/// during both slotting selection and transactional putaway confirmation.
/// </summary>
internal static class PutawaySafetyValidator
{
    public static decimal ResolveItemVolumeCm3(Item item) =>
        item.VolumeCm3 > 0m
            ? item.VolumeCm3
            : item.LengthCm * item.WidthCm * item.HeightCm;

    public static async Task ValidateDestinationSafetyAndCapacityAsync(
        IApplicationDbContext context,
        Location destination,
        Item item,
        decimal incomingQuantity,
        CancellationToken cancellationToken)
    {
        var zone = destination.Zone;
        if (item.StorageTempClass == StorageTempClass.Frozen)
        {
            var isFrozenZone = zone != null &&
                (zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= -15m));
            if (!isFrozenZone)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Frozen SKU '{item.Sku}' cannot be put away into non-freezer location '{destination.LocationCode}'.");
            }
        }
        else if (item.StorageTempClass == StorageTempClass.Refrigerated)
        {
            var isChilledZone = zone != null &&
                (zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= 8m));
            if (!isChilledZone)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Refrigerated SKU '{item.Sku}' cannot be put away into non-chilled location '{destination.LocationCode}'.");
            }
        }
        else if (zone != null && zone.ZoneType == WarehouseZoneType.ColdStorage)
        {
            var allowsOverflow = await context.PutawayStrategyRules
                .AsNoTracking()
                .AnyAsync(r => r.FacilityId == destination.FacilityId &&
                               (r.ClientId == item.ClientId || r.ClientId == Guid.Empty) &&
                               r.IsActive &&
                               !r.IsDeleted &&
                               r.TempClassRequirement == StorageTempClass.Ambient &&
                               r.AllowDynamicOverflow,
                    cancellationToken);

            if (!allowsOverflow)
            {
                throw new ValidationException(
                    nameof(destination),
                    $"Ambient SKU '{item.Sku}' cannot be placed into cold storage location '{destination.LocationCode}' unless dynamic overflow is permitted by putaway strategy.");
            }
        }

        if (!destination.MaxWeightKg.HasValue && !destination.MaxVolumeCm3.HasValue)
        {
            return;
        }

        var existingStocks = await context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Where(s => s.FacilityId == destination.FacilityId && s.LocationId == destination.Id)
            .ToListAsync(cancellationToken);

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
}
