using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record TrackPackageShipmentQuery(Guid PackageId) : IRequest<CarrierTrackingResponse>;

public class TrackPackageShipmentQueryHandler : IRequestHandler<TrackPackageShipmentQuery, CarrierTrackingResponse>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public TrackPackageShipmentQueryHandler(
        IShippingLabelService shippingLabelService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _shippingLabelService = shippingLabelService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<CarrierTrackingResponse> Handle(
        TrackPackageShipmentQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(TrackPackageShipmentQuery));

        var package = await _context.Packages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == query.PackageId && !p.IsDeleted)
            .Select(p => new { p.Id, p.ClientId, p.FacilityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), query.PackageId);
        }

        // Transition allowance: Client users may track their own packages read-only with active ClientFacilityAccess until 7.4.0C dispatch:view is introduced.
        await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
            nameof(Package),
            package.Id,
            package.ClientId,
            package.FacilityId,
            allowClient: true,
            isMutation: false,
            cancellationToken);

        return await _shippingLabelService.TrackPackageAsync(query.PackageId, cancellationToken);
    }
}

