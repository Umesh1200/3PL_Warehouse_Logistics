using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetPackageTrackingTimelineQuery(Guid PackageId) : IRequest<PackageTrackingTimelineDto?>;

public class GetPackageTrackingTimelineQueryHandler : IRequestHandler<GetPackageTrackingTimelineQuery, PackageTrackingTimelineDto?>
{
    private readonly IShipmentTrackingService _trackingService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackageTrackingTimelineQueryHandler(
        IShipmentTrackingService trackingService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _trackingService = trackingService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PackageTrackingTimelineDto?> Handle(
        GetPackageTrackingTimelineQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetPackageTrackingTimelineQuery));

        var package = await _context.Packages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == query.PackageId && !p.IsDeleted)
            .Select(p => new { p.Id, p.ClientId, p.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Package), query.PackageId);

        await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
            nameof(Package),
            package.Id,
            package.ClientId,
            package.FacilityId,
            allowClient: true,
            isMutation: false,
            cancellationToken);

        return await _trackingService.GetPackageTrackingTimelineAsync(query.PackageId, cancellationToken);
    }
}

