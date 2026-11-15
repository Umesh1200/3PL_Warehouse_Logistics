using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Application.Common.Interfaces;

public class PickRouteOptions
{
    public bool ReverseAisleOrder { get; set; } = false;
    public string? StartLocationCode { get; set; }
    public string? DestinationLocationCode { get; set; }
}

public class PickRouteOptimizationResult
{
    public IReadOnlyList<PickTask> SequencedTasks { get; set; } = [];
    public int TotalStops { get; set; }
    public decimal EstimatedTravelMeters { get; set; }
    public IReadOnlyList<string> InaccessibleLocationWarnings { get; set; } = [];
    public string StrategyUsed { get; set; } = string.Empty;
}

public interface IPickRouteOptimizer
{
    string StrategyName { get; }

    PickRouteOptimizationResult OptimizeRoute(
        IReadOnlyList<PickTask> tasks,
        IReadOnlyDictionary<Guid, Location> locationMap,
        PickRouteOptions? options = null);
}

