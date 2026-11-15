using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record ReportShortPickCommand(
    Guid PickTaskId,
    decimal QuantityPicked,
    ShortPickReason Reason,
    string? Notes = null,
    string? ScannedLocationBarcode = null,
    string? ScannedItemBarcode = null,
    string? ScannedToteLpn = null,
    string? IdempotencyKey = null) : IRequest<ShortPickExecutionResult>;

public class ReportShortPickCommandHandler : IRequestHandler<ReportShortPickCommand, ShortPickExecutionResult>
{
    private readonly IPickExecutionService _executionService;
    private readonly ICurrentUserContext _currentUser;

    public ReportShortPickCommandHandler(
        IPickExecutionService executionService,
        ICurrentUserContext currentUser)
    {
        _executionService = executionService;
        _currentUser = currentUser;
    }

    public async Task<ShortPickExecutionResult> Handle(
        ReportShortPickCommand command,
        CancellationToken cancellationToken)
    {
        return await _executionService.ReportShortPickAsync(new ReportShortPickRequest
        {
            PickTaskId = command.PickTaskId,
            QuantityPicked = command.QuantityPicked,
            Reason = command.Reason,
            Notes = command.Notes,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedToteLpn = command.ScannedToteLpn,
            UserId = _currentUser.UserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

