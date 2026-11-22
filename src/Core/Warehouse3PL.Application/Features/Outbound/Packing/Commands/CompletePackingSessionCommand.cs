using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record CompletePackingSessionCommand(
    Guid PackingSessionId) : IRequest<CompletePackingSessionResult>;

public class CompletePackingSessionCommandHandler : IRequestHandler<CompletePackingSessionCommand, CompletePackingSessionResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public CompletePackingSessionCommandHandler(
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

    public async Task<CompletePackingSessionResult> Handle(
        CompletePackingSessionCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(CompletePackingSessionCommand));

        var session = await _context.PackingSessions
            .AsNoTracking()
            .Select(s => new { s.Id, s.FacilityId })
            .FirstOrDefaultAsync(s => s.Id == command.PackingSessionId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackingSession), command.PackingSessionId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(session.FacilityId, nameof(CompletePackingSessionCommand));

        return await _executionService.CompletePackingSessionAsync(new CompletePackingSessionRequest
        {
            PackingSessionId = command.PackingSessionId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

