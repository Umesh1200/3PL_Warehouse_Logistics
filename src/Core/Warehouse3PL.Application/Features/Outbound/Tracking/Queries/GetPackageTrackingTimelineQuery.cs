using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetPackageTrackingTimelineQuery(Guid PackageId) : IRequest<PackageTrackingTimelineDto?>;

public class GetPackageTrackingTimelineQueryHandler : IRequestHandler<GetPackageTrackingTimelineQuery, PackageTrackingTimelineDto?>
{
    private readonly IShipmentTrackingService _trackingService;

    public GetPackageTrackingTimelineQueryHandler(IShipmentTrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    public async Task<PackageTrackingTimelineDto?> Handle(
        GetPackageTrackingTimelineQuery query,
        CancellationToken cancellationToken)
    {
        return await _trackingService.GetPackageTrackingTimelineAsync(query.PackageId, cancellationToken);
    }
}

