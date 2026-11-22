using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record PackItemCommand(
    Guid PackingSessionId,
    Guid PackageId,
    string ScannedToteLpn,
    string ScannedItemBarcode,
    string? ScannedLotNumber,
    decimal Quantity,
    string? IdempotencyKey = null) : IRequest<PackItemResult>;

public class PackItemCommandHandler : IRequestHandler<PackItemCommand, PackItemResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public PackItemCommandHandler(
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

    public async Task<PackItemResult> Handle(
        PackItemCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(PackItemCommand));

        var session = await _context.PackingSessions
            .AsNoTracking()
            .Select(s => new { s.Id, s.FacilityId })
            .FirstOrDefaultAsync(s => s.Id == command.PackingSessionId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackingSession), command.PackingSessionId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(session.FacilityId, nameof(PackItemCommand));

        return await _executionService.PackItemAsync(new PackItemRequest
        {
            PackingSessionId = command.PackingSessionId,
            PackageId = command.PackageId,
            ScannedToteLpn = command.ScannedToteLpn,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedLotNumber = command.ScannedLotNumber,
            Quantity = command.Quantity,
            UserId = _currentUser.UserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

