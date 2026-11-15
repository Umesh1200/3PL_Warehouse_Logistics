using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record TrackPackageShipmentQuery(Guid PackageId) : IRequest<CarrierTrackingResponse>;

public class TrackPackageShipmentQueryHandler : IRequestHandler<TrackPackageShipmentQuery, CarrierTrackingResponse>
{
    private readonly IShippingLabelService _shippingLabelService;

    public TrackPackageShipmentQueryHandler(IShippingLabelService shippingLabelService)
    {
        _shippingLabelService = shippingLabelService;
    }

    public async Task<CarrierTrackingResponse> Handle(
        TrackPackageShipmentQuery query,
        CancellationToken cancellationToken)
    {
        return await _shippingLabelService.TrackPackageAsync(query.PackageId, cancellationToken);
    }
}

