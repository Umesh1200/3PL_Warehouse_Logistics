using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Putaway.Queries.RecommendPutawayLocation;

public record RecommendPutawayLocationQuery(
    Guid ClientId,
    Guid FacilityId,
    Guid ItemId,
    decimal Quantity,
    Guid? LotId = null) : IRequest<List<PutawayRecommendation>>;

public class RecommendPutawayLocationQueryHandler : IRequestHandler<RecommendPutawayLocationQuery, List<PutawayRecommendation>>
{
    private readonly IPutawayRecommendationService _putawayService;

    public RecommendPutawayLocationQueryHandler(IPutawayRecommendationService putawayService)
    {
        _putawayService = putawayService;
    }

    public async Task<List<PutawayRecommendation>> Handle(RecommendPutawayLocationQuery request, CancellationToken cancellationToken)
    {
        return await _putawayService.RecommendLocationsAsync(
            request.ClientId,
            request.FacilityId,
            request.ItemId,
            request.Quantity,
            request.LotId,
            cancellationToken);
    }
}

