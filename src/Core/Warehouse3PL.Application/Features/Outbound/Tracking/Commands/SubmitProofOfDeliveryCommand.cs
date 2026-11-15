using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SubmitProofOfDeliveryCommand(SubmitProofOfDeliveryRequest Request) : IRequest<SubmitProofOfDeliveryResult>;

public class SubmitProofOfDeliveryCommandHandler : IRequestHandler<SubmitProofOfDeliveryCommand, SubmitProofOfDeliveryResult>
{
    private readonly IProofOfDeliveryService _podService;

    public SubmitProofOfDeliveryCommandHandler(IProofOfDeliveryService podService)
    {
        _podService = podService;
    }

    public Task<SubmitProofOfDeliveryResult> Handle(SubmitProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        return _podService.SubmitProofOfDeliveryAsync(command.Request, cancellationToken);
    }
}

