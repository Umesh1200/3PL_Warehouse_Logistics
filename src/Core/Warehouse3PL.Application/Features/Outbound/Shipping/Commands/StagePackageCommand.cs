using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record StagePackageCommand(
    Guid PackageId,
    Guid StagingLocationId) : IRequest<StagePackageResult>;

public class StagePackageCommandHandler : IRequestHandler<StagePackageCommand, StagePackageResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public StagePackageCommandHandler(
        IShipmentConsolidationService consolidationService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _consolidationService = consolidationService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<StagePackageResult> Handle(
        StagePackageCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(StagePackageCommand));

        var package = await _context.Packages
            .AsNoTracking()
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(p => p.Id == command.PackageId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Package), command.PackageId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(package.FacilityId, nameof(StagePackageCommand));

        return await _consolidationService.StagePackageAsync(new StagePackageRequest
        {
            PackageId = command.PackageId,
            StagingLocationId = command.StagingLocationId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

