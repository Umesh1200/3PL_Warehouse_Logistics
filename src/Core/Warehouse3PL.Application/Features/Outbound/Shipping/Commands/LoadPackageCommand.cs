using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record LoadPackageCommand(
    Guid PackageId,
    Guid? ShipmentId = null,
    string? TrailerNumber = null,
    Guid? DockLocationId = null) : IRequest<LoadPackageResult>;

public class LoadPackageCommandHandler : IRequestHandler<LoadPackageCommand, LoadPackageResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public LoadPackageCommandHandler(
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

    public async Task<LoadPackageResult> Handle(
        LoadPackageCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(LoadPackageCommand));

        var package = await _context.Packages
            .AsNoTracking()
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(p => p.Id == command.PackageId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Package), command.PackageId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(package.FacilityId, nameof(LoadPackageCommand));

        return await _consolidationService.LoadPackageAsync(new LoadPackageRequest
        {
            PackageId = command.PackageId,
            ShipmentId = command.ShipmentId,
            TrailerNumber = command.TrailerNumber,
            DockLocationId = command.DockLocationId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

