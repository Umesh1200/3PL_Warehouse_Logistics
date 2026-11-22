using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Queries;

public record GetProofOfDeliveryQuery(
    Guid? PackageId = null,
    Guid? ProofOfDeliveryId = null,
    TimeSpan? UrlExpiry = null) : IRequest<ProofOfDeliveryDto?>;

public class GetProofOfDeliveryQueryHandler : IRequestHandler<GetProofOfDeliveryQuery, ProofOfDeliveryDto?>
{
    private readonly IProofOfDeliveryService _podService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetProofOfDeliveryQueryHandler(
        IProofOfDeliveryService podService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _podService = podService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<ProofOfDeliveryDto?> Handle(GetProofOfDeliveryQuery query, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureIdentityAllowed(allowClient: true, nameof(GetProofOfDeliveryQuery));

        if (query.PackageId.HasValue)
        {
            var package = await _context.Packages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.Id == query.PackageId.Value && !p.IsDeleted)
                .Select(p => new { p.Id, p.ClientId, p.FacilityId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Package), query.PackageId.Value);

            await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
                nameof(Package),
                package.Id,
                package.ClientId,
                package.FacilityId,
                allowClient: true,
                isMutation: false,
                cancellationToken);

            return await _podService.GetProofOfDeliveryByPackageIdAsync(query.PackageId.Value, query.UrlExpiry, cancellationToken);
        }

        if (query.ProofOfDeliveryId.HasValue)
        {
            var pod = await _context.ProofOfDeliveries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.Id == query.ProofOfDeliveryId.Value)
                .Select(p => new { p.Id, p.ClientId, p.FacilityId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(ProofOfDelivery), query.ProofOfDeliveryId.Value);

            await _outboundAuth.ValidateEntityReadOrClientMutationAsync(
                nameof(ProofOfDelivery),
                pod.Id,
                pod.ClientId,
                pod.FacilityId,
                allowClient: true,
                isMutation: false,
                cancellationToken);

            return await _podService.GetProofOfDeliveryByIdAsync(query.ProofOfDeliveryId.Value, query.UrlExpiry, cancellationToken);
        }

        return null;
    }
}

