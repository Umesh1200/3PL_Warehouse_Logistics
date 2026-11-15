using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record GenerateShippingLabelCommand(
    Guid PackageId,
    string CarrierCode,
    CarrierServiceLevel ServiceLevel,
    string LabelFormat = "ZPL",
    string? IdempotencyKey = null,
    bool SimulateFailure = false) : IRequest<GenerateShippingLabelResult>;

public class GenerateShippingLabelCommandHandler : IRequestHandler<GenerateShippingLabelCommand, GenerateShippingLabelResult>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly ICurrentUserContext _currentUser;

    public GenerateShippingLabelCommandHandler(
        IShippingLabelService shippingLabelService,
        ICurrentUserContext currentUser)
    {
        _shippingLabelService = shippingLabelService;
        _currentUser = currentUser;
    }

    public async Task<GenerateShippingLabelResult> Handle(
        GenerateShippingLabelCommand command,
        CancellationToken cancellationToken)
    {
        return await _shippingLabelService.GenerateLabelAsync(new GenerateShippingLabelRequest
        {
            PackageId = command.PackageId,
            CarrierCode = command.CarrierCode,
            ServiceLevel = command.ServiceLevel,
            LabelFormat = command.LabelFormat,
            IdempotencyKey = command.IdempotencyKey,
            SimulateFailure = command.SimulateFailure,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

