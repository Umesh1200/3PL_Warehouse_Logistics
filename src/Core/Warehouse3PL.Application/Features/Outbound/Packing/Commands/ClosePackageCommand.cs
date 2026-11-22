using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record ClosePackageCommand(
    Guid PackageId,
    decimal ScaleGrossWeightKg) : IRequest<ClosePackageResult>;

public class ClosePackageCommandHandler : IRequestHandler<ClosePackageCommand, ClosePackageResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ClosePackageCommandHandler(
        IPackingExecutionService executionService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _executionService = executionService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<ClosePackageResult> Handle(
        ClosePackageCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ClosePackageCommand));

        var package = await _context.Packages
            .AsNoTracking()
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(p => p.Id == command.PackageId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Package), command.PackageId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(package.FacilityId, nameof(ClosePackageCommand));

        return await _executionService.ClosePackageAsync(new ClosePackageRequest
        {
            PackageId = command.PackageId,
            ScaleGrossWeightKg = command.ScaleGrossWeightKg,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

