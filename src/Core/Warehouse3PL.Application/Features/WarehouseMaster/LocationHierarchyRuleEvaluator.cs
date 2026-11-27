using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

/// <summary>
/// Evaluates configurable parent->child LocationType hierarchy rules with deterministic
/// facility-override over global-default precedence (MDM-2.1 A02 & A04).
/// </summary>
public static class LocationHierarchyRuleEvaluator
{
    public const int HardMaxHierarchyDepth = 10;

    private static readonly HashSet<(LocationType? Parent, LocationType Child)> BuiltInDefaults =
    [
        // Root rules (compact & deep)
        (null, LocationType.Facility),
        (null, LocationType.Zone),
        (null, LocationType.Mezzanine),
        (null, LocationType.Aisle),
        (null, LocationType.Rack),
        (null, LocationType.ShelfLevel),
        (null, LocationType.Bin),
        (null, LocationType.BulkFloor),
        (null, LocationType.Cantilever),
        (null, LocationType.DockDoor),
        (null, LocationType.StagingLane),
        (null, LocationType.PackingStation),
        // Transition rules
        (LocationType.Facility, LocationType.Zone),
        (LocationType.Facility, LocationType.Mezzanine),
        (LocationType.Facility, LocationType.Aisle),
        (LocationType.Facility, LocationType.Rack),
        (LocationType.Facility, LocationType.Bin),
        (LocationType.Facility, LocationType.BulkFloor),
        (LocationType.Facility, LocationType.Cantilever),
        (LocationType.Facility, LocationType.DockDoor),
        (LocationType.Facility, LocationType.StagingLane),
        (LocationType.Facility, LocationType.PackingStation),
        (LocationType.Zone, LocationType.Mezzanine),
        (LocationType.Zone, LocationType.Aisle),
        (LocationType.Zone, LocationType.Rack),
        (LocationType.Zone, LocationType.Bin),
        (LocationType.Zone, LocationType.BulkFloor),
        (LocationType.Zone, LocationType.Cantilever),
        (LocationType.Zone, LocationType.DockDoor),
        (LocationType.Zone, LocationType.StagingLane),
        (LocationType.Zone, LocationType.PackingStation),
        (LocationType.Mezzanine, LocationType.Aisle),
        (LocationType.Mezzanine, LocationType.Rack),
        (LocationType.Mezzanine, LocationType.ShelfLevel),
        (LocationType.Mezzanine, LocationType.Bin),
        (LocationType.Mezzanine, LocationType.BulkFloor),
        (LocationType.Mezzanine, LocationType.PackingStation),
        (LocationType.Aisle, LocationType.Rack),
        (LocationType.Aisle, LocationType.ShelfLevel),
        (LocationType.Aisle, LocationType.Bin),
        (LocationType.Aisle, LocationType.Cantilever),
        (LocationType.Aisle, LocationType.BulkFloor),
        (LocationType.Rack, LocationType.ShelfLevel),
        (LocationType.Rack, LocationType.Bin),
        (LocationType.Rack, LocationType.Cantilever),
        (LocationType.ShelfLevel, LocationType.Bin)
    ];

    public static async Task ValidatePlacementAsync(
        IApplicationDbContext db,
        Guid facilityId,
        LocationType? parentLocationType,
        LocationType childLocationType,
        int targetHierarchyLevel,
        CancellationToken cancellationToken)
    {
        if (targetHierarchyLevel < 1 || targetHierarchyLevel > HardMaxHierarchyDepth)
        {
            throw new ValidationException(
                "ParentLocationId",
                $"Location hierarchy depth {targetHierarchyLevel} exceeds the maximum allowed depth of {HardMaxHierarchyDepth}.");
        }

        // Load facility-specific override first, then global rule
        var matchingRules = await db.LocationHierarchyRules
            .AsNoTracking()
            .Where(r => (r.FacilityId == facilityId || r.FacilityId == null) &&
                        r.ParentLocationType == parentLocationType &&
                        r.ChildLocationType == childLocationType)
            .ToListAsync(cancellationToken);

        var facilityRule = matchingRules.FirstOrDefault(r => r.FacilityId == facilityId);
        if (facilityRule != null)
        {
            if (!facilityRule.IsActive)
            {
                throw new ValidationException(
                    "LocationType",
                    $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is explicitly disabled for facility '{facilityId}'.");
            }

            if (targetHierarchyLevel > facilityRule.MaxDepth)
            {
                throw new ValidationException(
                    "ParentLocationId",
                    $"Location depth {targetHierarchyLevel} exceeds facility rule MaxDepth {facilityRule.MaxDepth} for '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}'.");
            }

            return;
        }

        var globalRule = matchingRules.FirstOrDefault(r => r.FacilityId == null);
        if (globalRule != null)
        {
            if (!globalRule.IsActive)
            {
                throw new ValidationException(
                    "LocationType",
                    $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is disabled by global hierarchy rules.");
            }

            if (targetHierarchyLevel > globalRule.MaxDepth)
            {
                throw new ValidationException(
                    "ParentLocationId",
                    $"Location depth {targetHierarchyLevel} exceeds global rule MaxDepth {globalRule.MaxDepth} for '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}'.");
            }

            return;
        }

        // If global rules exist in DB (all migrated PostgreSQL DBs), missing transition is rejected (fail-closed).
        // If no global rules exist in DB (minimal InMemory unit test without migration seed), evaluate against built-in defaults.
        var anyGlobalRulesInDb = await db.LocationHierarchyRules
            .AsNoTracking()
            .AnyAsync(r => r.FacilityId == null, cancellationToken);
        if (anyGlobalRulesInDb)
        {
            throw new ValidationException(
                "LocationType",
                $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is not permitted by configured hierarchy rules.");
        }

        if (!BuiltInDefaults.Contains((parentLocationType, childLocationType)))
        {
            throw new ValidationException(
                "LocationType",
                $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is not permitted by default hierarchy rules.");
        }
    }
}
