using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record ResolvedHierarchyRule(int MaxDepth, bool IsStorableLeaf);

/// <summary>
/// Evaluates configurable parent->child LocationType hierarchy rules with deterministic
/// facility-override over global-default precedence, configurable MaxDepth enforcement,
/// and explicit IsStorableLeaf semantics (MDM-2.1 A02, A04 & MDM21-R02).
/// </summary>
public static class LocationHierarchyRuleEvaluator
{
    public const int HardMaxHierarchyDepth = 10;

    private static readonly Dictionary<(LocationType? Parent, LocationType Child), ResolvedHierarchyRule> BuiltInDefaults = new()
    {
        // Root rules (compact & deep)
        [(null, LocationType.Facility)] = new(10, false),
        [(null, LocationType.Zone)] = new(10, false),
        [(null, LocationType.Mezzanine)] = new(10, false),
        [(null, LocationType.Aisle)] = new(10, false),
        [(null, LocationType.Rack)] = new(10, false),
        [(null, LocationType.ShelfLevel)] = new(10, false),
        [(null, LocationType.Bin)] = new(10, true),
        [(null, LocationType.BulkFloor)] = new(10, true),
        [(null, LocationType.Cantilever)] = new(10, true),
        [(null, LocationType.DockDoor)] = new(10, false),
        [(null, LocationType.StagingLane)] = new(10, true),
        [(null, LocationType.PackingStation)] = new(10, false),
        // Transition rules
        [(LocationType.Facility, LocationType.Zone)] = new(10, false),
        [(LocationType.Facility, LocationType.Mezzanine)] = new(10, false),
        [(LocationType.Facility, LocationType.Aisle)] = new(10, false),
        [(LocationType.Facility, LocationType.Rack)] = new(10, false),
        [(LocationType.Facility, LocationType.Bin)] = new(10, true),
        [(LocationType.Facility, LocationType.BulkFloor)] = new(10, true),
        [(LocationType.Facility, LocationType.Cantilever)] = new(10, true),
        [(LocationType.Facility, LocationType.DockDoor)] = new(10, false),
        [(LocationType.Facility, LocationType.StagingLane)] = new(10, true),
        [(LocationType.Facility, LocationType.PackingStation)] = new(10, false),
        [(LocationType.Zone, LocationType.Mezzanine)] = new(10, false),
        [(LocationType.Zone, LocationType.Aisle)] = new(10, false),
        [(LocationType.Zone, LocationType.Rack)] = new(10, false),
        [(LocationType.Zone, LocationType.Bin)] = new(10, true),
        [(LocationType.Zone, LocationType.BulkFloor)] = new(10, true),
        [(LocationType.Zone, LocationType.Cantilever)] = new(10, true),
        [(LocationType.Zone, LocationType.DockDoor)] = new(10, false),
        [(LocationType.Zone, LocationType.StagingLane)] = new(10, true),
        [(LocationType.Zone, LocationType.PackingStation)] = new(10, false),
        [(LocationType.Mezzanine, LocationType.Aisle)] = new(10, false),
        [(LocationType.Mezzanine, LocationType.Rack)] = new(10, false),
        [(LocationType.Mezzanine, LocationType.ShelfLevel)] = new(10, false),
        [(LocationType.Mezzanine, LocationType.Bin)] = new(10, true),
        [(LocationType.Mezzanine, LocationType.BulkFloor)] = new(10, true),
        [(LocationType.Mezzanine, LocationType.PackingStation)] = new(10, false),
        [(LocationType.Aisle, LocationType.Rack)] = new(10, false),
        [(LocationType.Aisle, LocationType.ShelfLevel)] = new(10, false),
        [(LocationType.Aisle, LocationType.Bin)] = new(10, true),
        [(LocationType.Aisle, LocationType.Cantilever)] = new(10, true),
        [(LocationType.Aisle, LocationType.BulkFloor)] = new(10, true),
        [(LocationType.Rack, LocationType.ShelfLevel)] = new(10, false),
        [(LocationType.Rack, LocationType.Bin)] = new(10, true),
        [(LocationType.Rack, LocationType.Cantilever)] = new(10, true),
        [(LocationType.ShelfLevel, LocationType.Bin)] = new(10, true)
    };

    public static async Task<ResolvedHierarchyRule> ValidatePlacementAsync(
        IApplicationDbContext db,
        Guid facilityId,
        LocationType? parentLocationType,
        LocationType childLocationType,
        int targetHierarchyLevel,
        CancellationToken cancellationToken,
        bool? requestedIsStorable = null)
    {
        if (targetHierarchyLevel < 1 || targetHierarchyLevel > HardMaxHierarchyDepth)
        {
            throw new ValidationException(
                "ParentLocationId",
                $"Location hierarchy depth {targetHierarchyLevel} exceeds the maximum allowed depth of {HardMaxHierarchyDepth}.");
        }

        var matchingRules = await db.LocationHierarchyRules
            .AsNoTracking()
            .Where(r => (r.FacilityId == facilityId || r.FacilityId == null) &&
                        r.ParentLocationType == parentLocationType &&
                        r.ChildLocationType == childLocationType)
            .ToListAsync(cancellationToken);

        ResolvedHierarchyRule resolved;
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

            resolved = new ResolvedHierarchyRule(facilityRule.MaxDepth, facilityRule.IsStorableLeaf);
        }
        else
        {
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

                resolved = new ResolvedHierarchyRule(globalRule.MaxDepth, globalRule.IsStorableLeaf);
            }
            else
            {
                var anyGlobalRulesInDb = await db.LocationHierarchyRules
                    .AsNoTracking()
                    .AnyAsync(r => r.FacilityId == null, cancellationToken);
                if (anyGlobalRulesInDb)
                {
                    throw new ValidationException(
                        "LocationType",
                        $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is not permitted by configured hierarchy rules.");
                }

                if (!BuiltInDefaults.TryGetValue((parentLocationType, childLocationType), out resolved!))
                {
                    throw new ValidationException(
                        "LocationType",
                        $"Location transition '{parentLocationType?.ToString() ?? "ROOT"} -> {childLocationType}' is not permitted by default hierarchy rules.");
                }
            }
        }

        // MDM21-R02: Enforce explicit IsStorableLeaf semantics
        if (requestedIsStorable == true && !resolved.IsStorableLeaf)
        {
            throw new ValidationException(
                "IsStorable",
                $"Location type '{childLocationType}' under '{parentLocationType?.ToString() ?? "ROOT"}' is not configured as a storable leaf (IsStorableLeaf = false).");
        }

        return resolved;
    }

    /// <summary>
    /// Validates a proposed rule change against existing locations in memory so no rule change can
    /// silently invalidate existing hierarchies (MDM21-R02).
    /// </summary>
    public static void ValidateExistingLocationsAgainstProposedRule(
        IEnumerable<Location> existingLocations,
        Dictionary<Guid, Location> allById,
        LocationType? ruleParentType,
        LocationType ruleChildType,
        int proposedMaxDepth,
        bool proposedIsStorableLeaf,
        bool proposedIsActive)
    {
        foreach (var loc in existingLocations)
        {
            if (loc.IsDeleted) continue;

            LocationType? actualParentType = null;
            if (loc.ParentLocationId.HasValue && allById.TryGetValue(loc.ParentLocationId.Value, out var parent))
            {
                actualParentType = parent.LocationType;
            }

            if (actualParentType != ruleParentType || loc.LocationType != ruleChildType)
            {
                continue;
            }

            if (!proposedIsActive)
            {
                throw new DomainException(
                    $"Cannot disable hierarchy rule '{ruleParentType?.ToString() ?? "ROOT"} -> {ruleChildType}' because active/existing location '{loc.LocationCode}' depends on it.",
                    "HIERARCHY_RULE_CONFLICTS_EXISTING_LOCATIONS");
            }

            if (loc.HierarchyLevel > proposedMaxDepth)
            {
                throw new DomainException(
                    $"Cannot reduce MaxDepth to {proposedMaxDepth} for '{ruleParentType?.ToString() ?? "ROOT"} -> {ruleChildType}' because existing location '{loc.LocationCode}' resides at depth {loc.HierarchyLevel}.",
                    "HIERARCHY_RULE_CONFLICTS_EXISTING_LOCATIONS");
            }

            if (!proposedIsStorableLeaf && loc.IsStorable)
            {
                throw new DomainException(
                    $"Cannot set IsStorableLeaf = false for '{ruleParentType?.ToString() ?? "ROOT"} -> {ruleChildType}' because existing storable location '{loc.LocationCode}' depends on it.",
                    "HIERARCHY_RULE_CONFLICTS_EXISTING_LOCATIONS");
            }
        }
    }
}
