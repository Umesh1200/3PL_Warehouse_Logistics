using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Queries;

public record GetCartonizationRecommendationQuery(
    Guid ClientId,
    Guid FacilityId,
    Guid? OutboundOrderId = null,
    List<CartonizationItemRequest>? Items = null,
    Guid? PreferredPackagingMaterialId = null,
    decimal VolumeUtilizationFactor = 0.85m) : IRequest<CartonizationRecommendation>;

public class GetCartonizationRecommendationQueryHandler : IRequestHandler<GetCartonizationRecommendationQuery, CartonizationRecommendation>
{
    private readonly ICartonizationService _cartonizationService;

    public GetCartonizationRecommendationQueryHandler(ICartonizationService cartonizationService)
    {
        _cartonizationService = cartonizationService;
    }

    public async Task<CartonizationRecommendation> Handle(
        GetCartonizationRecommendationQuery query,
        CancellationToken cancellationToken)
    {
        return await _cartonizationService.RecommendCartonizationAsync(new CartonizationRequest
        {
            ClientId = query.ClientId,
            FacilityId = query.FacilityId,
            OutboundOrderId = query.OutboundOrderId,
            Items = query.Items ?? [],
            PreferredPackagingMaterialId = query.PreferredPackagingMaterialId,
            VolumeUtilizationFactor = query.VolumeUtilizationFactor
        }, cancellationToken);
    }
}

