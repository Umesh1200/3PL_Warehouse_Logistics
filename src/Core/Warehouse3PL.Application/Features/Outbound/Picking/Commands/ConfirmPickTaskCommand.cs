using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record ConfirmPickTaskCommand(
    Guid PickTaskId,
    string ScannedLocationBarcode,
    string ScannedItemBarcode,
    string? ScannedLotNumber,
    string ScannedToteLpn,
    decimal Quantity,
    string? IdempotencyKey = null) : IRequest<PickConfirmationResult>;

public class ConfirmPickTaskCommandHandler : IRequestHandler<ConfirmPickTaskCommand, PickConfirmationResult>
{
    private readonly IPickExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public ConfirmPickTaskCommandHandler(
        IPickExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<PickConfirmationResult> Handle(
        ConfirmPickTaskCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.ConfirmPickAsync(new ConfirmPickRequest
        {
            PickTaskId = command.PickTaskId,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedLotNumber = command.ScannedLotNumber,
            ScannedToteLpn = command.ScannedToteLpn,
            Quantity = command.Quantity,
            UserId = _currentUser.UserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

