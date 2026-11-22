using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record GenerateBillOfLadingCommand(
    Guid ShipmentId,
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? SpecialInstructions = null) : IRequest<GenerateBolResult>;

public class GenerateBillOfLadingCommandHandler : IRequestHandler<GenerateBillOfLadingCommand, GenerateBolResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GenerateBillOfLadingCommandHandler(
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

    public async Task<GenerateBolResult> Handle(
        GenerateBillOfLadingCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GenerateBillOfLadingCommand));

        var shipment = await _context.Shipments
            .AsNoTracking()
            .Select(s => new { s.Id, s.FacilityId })
            .FirstOrDefaultAsync(s => s.Id == command.ShipmentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Shipment), command.ShipmentId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(shipment.FacilityId, nameof(GenerateBillOfLadingCommand));

        return await _consolidationService.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = command.ShipmentId,
            TrailerNumber = command.TrailerNumber,
            SealNumber = command.SealNumber,
            SpecialInstructions = command.SpecialInstructions,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

