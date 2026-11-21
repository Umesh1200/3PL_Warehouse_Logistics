using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

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
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public RecommendPutawayLocationQueryHandler(
        IPutawayRecommendationService putawayService,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _putawayService = putawayService;
        _operationalAuth = operationalAuth;
    }

    public async Task<List<PutawayRecommendation>> Handle(RecommendPutawayLocationQuery request, CancellationToken cancellationToken)
    {
        await _operationalAuth.ValidateClientFacilityAccessAsync(
            request.ClientId, request.FacilityId, cancellationToken);

        return await _putawayService.RecommendLocationsAsync(
            request.ClientId,
            request.FacilityId,
            request.ItemId,
            request.Quantity,
            request.LotId,
            cancellationToken);
    }
}

