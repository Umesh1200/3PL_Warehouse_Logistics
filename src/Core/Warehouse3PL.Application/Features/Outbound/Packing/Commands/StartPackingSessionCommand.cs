using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Commands;

public record StartPackingSessionCommand(
    Guid FacilityId,
    Guid PackingStationLocationId,
    Guid OutboundOrderId) : IRequest<PackingSessionResult>;

public class StartPackingSessionCommandHandler : IRequestHandler<StartPackingSessionCommand, PackingSessionResult>
{
    private readonly IPackingExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public StartPackingSessionCommandHandler(
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

    public async Task<PackingSessionResult> Handle(
        StartPackingSessionCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.ValidateStaffMutationFacilityAccess(command.FacilityId, nameof(StartPackingSessionCommand));

        var order = await _context.OutboundOrders
            .AsNoTracking()
            .Select(o => new { o.Id, o.FacilityId })
            .FirstOrDefaultAsync(o => o.Id == command.OutboundOrderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(OutboundOrder), command.OutboundOrderId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(order.FacilityId, nameof(StartPackingSessionCommand));

        return await _executionService.StartOrResumeSessionAsync(new StartPackingSessionRequest
        {
            FacilityId = command.FacilityId,
            PackingStationLocationId = command.PackingStationLocationId,
            OutboundOrderId = command.OutboundOrderId,
            OperatorUserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

