using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetOrderDeliveryStatusQuery(Guid OrderId) : IRequest<EvaluateOrderDeliveryRollupResult?>;

public class GetOrderDeliveryStatusQueryHandler : IRequestHandler<GetOrderDeliveryStatusQuery, EvaluateOrderDeliveryRollupResult?>
{
    private readonly IShipmentTrackingService _trackingService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetOrderDeliveryStatusQueryHandler(
        IShipmentTrackingService trackingService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _trackingService = trackingService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<EvaluateOrderDeliveryRollupResult?> Handle(
        GetOrderDeliveryStatusQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetOrderDeliveryStatusQuery));

        var order = await _context.OutboundOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.Id == query.OrderId && !o.IsDeleted)
            .Select(o => new { o.Id, o.ClientId, o.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OutboundOrder), query.OrderId);

        await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
            nameof(OutboundOrder),
            order.Id,
            order.ClientId,
            order.FacilityId,
            allowClient: true,
            isMutation: false,
            cancellationToken);

        return await _trackingService.GetOrderDeliveryStatusAsync(query.OrderId, cancellationToken);
    }
}

