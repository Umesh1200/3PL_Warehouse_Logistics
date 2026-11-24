using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

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
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetCartonizationRecommendationQueryHandler(
        ICartonizationService cartonizationService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _cartonizationService = cartonizationService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<CartonizationRecommendation> Handle(
        GetCartonizationRecommendationQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.ValidateStaffReadFacilityAccess(query.FacilityId, nameof(GetCartonizationRecommendationQuery));

        if (query.OutboundOrderId.HasValue)
        {
            var order = await _context.OutboundOrders
                .AsNoTracking()
                .Where(o => o.Id == query.OutboundOrderId.Value && !o.IsDeleted)
                .Select(o => new { o.Id, o.FacilityId, o.ClientId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(OutboundOrder), query.OutboundOrderId.Value);

            if (order.FacilityId != query.FacilityId)
            {
                _outboundAuth.ValidateStaffMutationFacilityAccess(order.FacilityId, nameof(GetCartonizationRecommendationQuery));
                throw new ForbiddenException("Outbound order does not belong to the requested facility.");
            }

            if (query.ClientId != Guid.Empty && order.ClientId != query.ClientId)
            {
                throw new ValidationException(nameof(query.ClientId),
                    "ClientId does not match the outbound order owner.");
            }
        }

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

