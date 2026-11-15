using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Demand Matching Engine for detecting cross-dock opportunities between incoming freight and unfulfilled outbound orders.
/// </summary>
public interface ICrossDockEngine
{
    /// <summary>
    /// Evaluates staging inventory and inbound receipt lines against active outbound order demand.
    /// </summary>
    Task<List<CrossDockOpportunityDto>> DetectOpportunitiesAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? receivingSessionId = null,
        CancellationToken cancellationToken = default);
}

