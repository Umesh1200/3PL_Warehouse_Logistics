using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SupersedeProofOfDeliveryCommand(SupersedeProofOfDeliveryRequest Request) : IRequest<SubmitProofOfDeliveryResult>;

public class SupersedeProofOfDeliveryCommandHandler : IRequestHandler<SupersedeProofOfDeliveryCommand, SubmitProofOfDeliveryResult>
{
    private readonly IProofOfDeliveryService _podService;

    public SupersedeProofOfDeliveryCommandHandler(IProofOfDeliveryService podService)
    {
        _podService = podService;
    }

    public Task<SubmitProofOfDeliveryResult> Handle(SupersedeProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        return _podService.SupersedeProofOfDeliveryAsync(command.Request, cancellationToken);
    }
}

