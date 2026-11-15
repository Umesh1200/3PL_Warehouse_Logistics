using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Slotting & Location Allocation Engine for directed putaway operations.
/// </summary>
public interface IPutawayLocationSelector
{
    /// <summary>
    /// Selects the single best target storage location for an item based on temperature, velocity, capacity, and affinity rules.
    /// </summary>
    Task<Location?> SelectOptimalLocationAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        IEnumerable<Guid>? excludedLocationIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates and scores all eligible candidate storage locations for putaway.
    /// </summary>
    Task<List<PutawayRecommendation>> EvaluateCandidateLocationsAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        IEnumerable<Guid>? excludedLocationIds = null,
        CancellationToken cancellationToken = default);
}

