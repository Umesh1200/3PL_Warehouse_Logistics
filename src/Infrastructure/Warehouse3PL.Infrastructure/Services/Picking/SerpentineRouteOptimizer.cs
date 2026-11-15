using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Services.Picking;

/// <summary>
/// Deterministic Serpentine (S-Curve) warehouse pick route optimizer.
/// Traverses odd-numbered aisles in ascending bay sequence and even-numbered aisles in descending bay sequence,
/// minimizing deadhead travel and excessive vertical climbing.
/// DB-SPEC-3PL-2026 Phase 4.3 Step 2.
/// </summary>
public class SerpentineRouteOptimizer : IPickRouteOptimizer
{
    private readonly ILogger<SerpentineRouteOptimizer> _logger;
    private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);

    public string StrategyName => "Serpentine_S_Curve";

    public SerpentineRouteOptimizer(ILogger<SerpentineRouteOptimizer> logger)
    {
        _logger = logger;
    }

    public PickRouteOptimizationResult OptimizeRoute(
        IReadOnlyList<PickTask> tasks,
        IReadOnlyDictionary<Guid, Location> locationMap,
        PickRouteOptions? options = null)
    {
        if (tasks == null || tasks.Count == 0)
        {
            return new PickRouteOptimizationResult
            {
                SequencedTasks = [],
                TotalStops = 0,
                EstimatedTravelMeters = 0m,
                InaccessibleLocationWarnings = [],
                StrategyUsed = StrategyName
            };
        }

        var warnings = new List<string>();

        // Wrap each task with parsed routing coordinates
        var mappedTasks = tasks.Select(t =>
        {
            locationMap.TryGetValue(t.SourceLocationId, out var loc);
            loc ??= t.SourceLocation;

            if (loc != null)
            {
                if (loc.IsLocked || loc.IsDamaged || !loc.IsActive || !loc.IsPickable || loc.Locks.Any(l => l.IsActive))
                {
                    warnings.Add(
                        $"Location '{loc.LocationCode}' for SKU ID '{t.ItemId}' is restricted/inaccessible " +
                        $"(Locked: {loc.IsLocked}, Damaged: {loc.IsDamaged}, Pickable: {loc.IsPickable}).");
                }
            }

            var aisle = ParseAisleNumber(loc);
            var bay = ParseBayNumber(loc);
            var shelf = loc?.ShelfLevel ?? 1;
            var bin = loc?.BinNumber ?? 1;
            var pickSeq = loc?.PickSequence;

            return new TaskLocationContext(t, loc, aisle, bay, shelf, bin, pickSeq);
        }).ToList();

        // Group by Aisle
        var aisleGroups = mappedTasks
            .GroupBy(x => x.Aisle)
            .OrderBy(g => g.Key);

        var sortedAisleList = options?.ReverseAisleOrder == true
            ? aisleGroups.Reverse().ToList()
            : aisleGroups.ToList();

        var orderedTasks = new List<PickTask>();

        foreach (var group in sortedAisleList)
        {
            var aisleNumber = group.Key;
            var isOddAisle = Math.Abs(aisleNumber) % 2 != 0;

            IOrderedEnumerable<TaskLocationContext> aisleOrdered;

            if (isOddAisle)
            {
                // Odd Aisle: Ascending traversal (Forward)
                aisleOrdered = group
                    .OrderBy(x => x.PickSequence ?? x.Bay)
                    .ThenBy(x => x.Bay)
                    .ThenBy(x => x.Shelf)
                    .ThenBy(x => x.Bin)
                    .ThenBy(x => x.Location?.Id ?? Guid.Empty)
                    .ThenBy(x => x.Task.Id);
            }
            else
            {
                // Even Aisle: Descending traversal (Reverse / S-turn)
                aisleOrdered = group
                    .OrderByDescending(x => x.PickSequence ?? x.Bay)
                    .ThenByDescending(x => x.Bay)
                    .ThenBy(x => x.Shelf) // Keep shelf ascending within bay to avoid erratic ladder climbing
                    .ThenByDescending(x => x.Bin)
                    .ThenBy(x => x.Location?.Id ?? Guid.Empty)
                    .ThenBy(x => x.Task.Id);
            }

            orderedTasks.AddRange(aisleOrdered.Select(x => x.Task));
        }

        // Assign deterministic sequential PickSequence
        for (int i = 0; i < orderedTasks.Count; i++)
        {
            orderedTasks[i].PickSequence = i + 1;
        }

        // Calculate estimated travel distance
        var totalDistance = CalculateEstimatedTravelDistance(orderedTasks, locationMap);

        _logger.LogInformation(
            "Serpentine route optimized {Count} tasks across {Aisles} aisles. Estimated travel: {Distance:F1}m. Warnings: {WarningsCount}",
            orderedTasks.Count, sortedAisleList.Count, totalDistance, warnings.Count);

        return new PickRouteOptimizationResult
        {
            SequencedTasks = orderedTasks,
            TotalStops = orderedTasks.Count,
            EstimatedTravelMeters = totalDistance,
            InaccessibleLocationWarnings = warnings,
            StrategyUsed = StrategyName
        };
    }

    private static int ParseAisleNumber(Location? location)
    {
        if (location == null) return 0;

        if (!string.IsNullOrWhiteSpace(location.AisleCode))
        {
            var match = NumberPattern.Match(location.AisleCode);
            if (match.Success && int.TryParse(match.Value, out var aisleNum))
            {
                return aisleNum;
            }

            // Fallback for single letters: 'A' -> 1, 'B' -> 2
            var trimmed = location.AisleCode.Trim().ToUpperInvariant();
            if (trimmed.Length == 1 && trimmed[0] >= 'A' && trimmed[0] <= 'Z')
            {
                return trimmed[0] - 'A' + 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(location.LocationCode))
        {
            var match = NumberPattern.Match(location.LocationCode);
            if (match.Success && int.TryParse(match.Value, out var locNum))
            {
                return locNum;
            }
        }

        return 0;
    }

    private static int ParseBayNumber(Location? location)
    {
        if (location == null) return 0;

        if (!string.IsNullOrWhiteSpace(location.RackCode))
        {
            var match = NumberPattern.Match(location.RackCode);
            if (match.Success && int.TryParse(match.Value, out var bayNum))
            {
                return bayNum;
            }
        }

        if (location.BinNumber.HasValue)
        {
            return location.BinNumber.Value;
        }

        if (!string.IsNullOrWhiteSpace(location.LocationCode))
        {
            var matches = NumberPattern.Matches(location.LocationCode);
            if (matches.Count >= 2 && int.TryParse(matches[1].Value, out var secondNum))
            {
                return secondNum;
            }
        }

        return 0;
    }

    private static decimal CalculateEstimatedTravelDistance(
        IReadOnlyList<PickTask> tasks,
        IReadOnlyDictionary<Guid, Location> locationMap)
    {
        if (tasks.Count <= 1) return 0m;

        decimal distance = 0m;

        for (int i = 0; i < tasks.Count - 1; i++)
        {
            locationMap.TryGetValue(tasks[i].SourceLocationId, out var loc1);
            loc1 ??= tasks[i].SourceLocation;

            locationMap.TryGetValue(tasks[i + 1].SourceLocationId, out var loc2);
            loc2 ??= tasks[i + 1].SourceLocation;

            if (loc1?.XCoord != null && loc1?.YCoord != null &&
                loc2?.XCoord != null && loc2?.YCoord != null)
            {
                // Manhattan 3D distance between known coordinate coordinates
                var dx = Math.Abs(loc2.XCoord.Value - loc1.XCoord.Value);
                var dy = Math.Abs(loc2.YCoord.Value - loc1.YCoord.Value);
                var dz = Math.Abs((loc2.ZCoord ?? 0m) - (loc1.ZCoord ?? 0m));
                distance += (dx + dy + dz);
            }
            else
            {
                // Layout heuristic: 10m per aisle switch, 1.5m per bay move, 0.5m per shelf move
                var aisle1 = ParseAisleNumber(loc1);
                var aisle2 = ParseAisleNumber(loc2);
                var bay1 = ParseBayNumber(loc1);
                var bay2 = ParseBayNumber(loc2);
                var shelf1 = loc1?.ShelfLevel ?? 1;
                var shelf2 = loc2?.ShelfLevel ?? 1;

                var aisleDiff = Math.Abs(aisle2 - aisle1);
                var bayDiff = Math.Abs(bay2 - bay1);
                var shelfDiff = Math.Abs(shelf2 - shelf1);

                distance += (aisleDiff * 10m) + (bayDiff * 1.5m) + (shelfDiff * 0.5m);
            }
        }

        return Math.Round(distance, 2);
    }

    private record TaskLocationContext(
        PickTask Task,
        Location? Location,
        int Aisle,
        int Bay,
        int Shelf,
        int Bin,
        int? PickSequence);
}

