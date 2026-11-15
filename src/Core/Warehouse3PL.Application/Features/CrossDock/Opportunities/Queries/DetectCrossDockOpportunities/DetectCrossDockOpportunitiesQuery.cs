using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.CrossDock.Opportunities.Queries.DetectCrossDockOpportunities;

public record DetectCrossDockOpportunitiesQuery(
    Guid FacilityId,
    Guid? ClientId = null,
    Guid? ReceivingSessionId = null) : IRequest<List<CrossDockOpportunityDto>>;

public class DetectCrossDockOpportunitiesQueryHandler : IRequestHandler<DetectCrossDockOpportunitiesQuery, List<CrossDockOpportunityDto>>
{
    private readonly ICrossDockEngine _crossDockEngine;

    public DetectCrossDockOpportunitiesQueryHandler(ICrossDockEngine crossDockEngine)
    {
        _crossDockEngine = crossDockEngine;
    }

    public async Task<List<CrossDockOpportunityDto>> Handle(DetectCrossDockOpportunitiesQuery request, CancellationToken cancellationToken)
    {
        return await _crossDockEngine.DetectOpportunitiesAsync(
            request.FacilityId,
            request.ClientId,
            request.ReceivingSessionId,
            cancellationToken);
    }
}

