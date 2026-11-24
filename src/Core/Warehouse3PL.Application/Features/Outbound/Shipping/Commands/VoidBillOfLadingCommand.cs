using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record VoidBillOfLadingCommand(
    Guid BolId,
    string Reason) : IRequest<VoidBolResult>;

public class VoidBillOfLadingCommandHandler : IRequestHandler<VoidBillOfLadingCommand, VoidBolResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public VoidBillOfLadingCommandHandler(
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

    public async Task<VoidBolResult> Handle(
        VoidBillOfLadingCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(VoidBillOfLadingCommand));

        var bol = await _context.BillsOfLading
            .AsNoTracking()
            .Select(b => new { b.Id, b.FacilityId })
            .FirstOrDefaultAsync(b => b.Id == command.BolId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(BillOfLading), command.BolId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(bol.FacilityId, nameof(VoidBillOfLadingCommand));

        return await _consolidationService.VoidBillOfLadingAsync(new VoidBolRequest
        {
            BolId = command.BolId,
            Reason = command.Reason,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}
