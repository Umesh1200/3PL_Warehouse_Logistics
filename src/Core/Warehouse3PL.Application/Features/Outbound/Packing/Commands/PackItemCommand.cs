using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

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
    private readonly ICurrentUserContext _currentUser;

    public PackItemCommandHandler(
        IPackingExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<PackItemResult> Handle(
        PackItemCommand command,
        CancellationToken cancellationToken)
    {
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

