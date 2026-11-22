using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetShippingLabelByIdQuery(Guid LabelId) : IRequest<ShippingLabelDto?>;

public class GetShippingLabelByIdQueryHandler : IRequestHandler<GetShippingLabelByIdQuery, ShippingLabelDto?>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetShippingLabelByIdQueryHandler(
        IShippingLabelService shippingLabelService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _shippingLabelService = shippingLabelService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<ShippingLabelDto?> Handle(
        GetShippingLabelByIdQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetShippingLabelByIdQuery));

        var label = await _context.ShippingLabels
            .AsNoTracking()
            .Where(l => l.Id == query.LabelId)
            .Select(l => new { l.Id, FacilityId = l.Package.FacilityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (label == null)
        {
            throw new EntityNotFoundException(nameof(ShippingLabel), query.LabelId);
        }

        _outboundAuth.ValidateStaffReadFacilityAccess(label.FacilityId, nameof(GetShippingLabelByIdQuery));

        return await _shippingLabelService.GetLabelAsync(query.LabelId, cancellationToken);
    }
}

