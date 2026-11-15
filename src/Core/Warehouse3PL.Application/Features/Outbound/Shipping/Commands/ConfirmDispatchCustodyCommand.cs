using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record ConfirmDispatchCustodyCommand(
    Guid ShipmentId,
    string DriverName,
    string? DriverLicenseNumber,
    string DriverSignature,
    string? TrailerNumber = null,
    string? SealNumber = null,
    string? Notes = null,
    string? IdempotencyKey = null) : IRequest<ConfirmDispatchCustodyResult>;

public class ConfirmDispatchCustodyCommandHandler : IRequestHandler<ConfirmDispatchCustodyCommand, ConfirmDispatchCustodyResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;

    public ConfirmDispatchCustodyCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
    }

    public async Task<ConfirmDispatchCustodyResult> Handle(
        ConfirmDispatchCustodyCommand command,
        CancellationToken cancellationToken)
    {
        return await _consolidationService.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = command.ShipmentId,
            DriverName = command.DriverName,
            DriverLicenseNumber = command.DriverLicenseNumber,
            DriverSignature = command.DriverSignature,
            TrailerNumber = command.TrailerNumber,
            SealNumber = command.SealNumber,
            AuthorizedUserId = _currentUser.UserId ?? Guid.Empty,
            Notes = command.Notes,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

