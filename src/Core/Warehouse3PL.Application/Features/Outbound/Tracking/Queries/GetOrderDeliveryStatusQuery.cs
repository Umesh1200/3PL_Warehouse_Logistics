using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetOrderDeliveryStatusQuery(Guid OrderId) : IRequest<EvaluateOrderDeliveryRollupResult?>;

public class GetOrderDeliveryStatusQueryHandler : IRequestHandler<GetOrderDeliveryStatusQuery, EvaluateOrderDeliveryRollupResult?>
{
    private readonly IShipmentTrackingService _trackingService;

    public GetOrderDeliveryStatusQueryHandler(IShipmentTrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    public async Task<EvaluateOrderDeliveryRollupResult?> Handle(
        GetOrderDeliveryStatusQuery query,
        CancellationToken cancellationToken)
    {
        return await _trackingService.GetOrderDeliveryStatusAsync(query.OrderId, cancellationToken);
    }
}

