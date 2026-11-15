using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record VoidShippingLabelCommand(
    Guid LabelId,
    string Reason,
    string? IdempotencyKey = null,
    bool SimulateFailure = false) : IRequest<VoidShippingLabelResult>;

public class VoidShippingLabelCommandHandler : IRequestHandler<VoidShippingLabelCommand, VoidShippingLabelResult>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly ICurrentUserContext _currentUser;

    public VoidShippingLabelCommandHandler(
        IShippingLabelService shippingLabelService,
        ICurrentUserContext currentUser)
    {
        _shippingLabelService = shippingLabelService;
        _currentUser = currentUser;
    }

    public async Task<VoidShippingLabelResult> Handle(
        VoidShippingLabelCommand command,
        CancellationToken cancellationToken)
    {
        return await _shippingLabelService.VoidLabelAsync(new VoidShippingLabelRequest
        {
            LabelId = command.LabelId,
            Reason = command.Reason,
            IdempotencyKey = command.IdempotencyKey,
            SimulateFailure = command.SimulateFailure,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

