using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record IngestReturnTrackingEventCommand(IngestReturnTrackingRequest Request) : IRequest<IngestReturnTrackingResult>;

public class IngestReturnTrackingEventCommandHandler : IRequestHandler<IngestReturnTrackingEventCommand, IngestReturnTrackingResult>
{
    private readonly IReturnTrackingService _returnTrackingService;

    public IngestReturnTrackingEventCommandHandler(IReturnTrackingService returnTrackingService)
    {
        _returnTrackingService = returnTrackingService;
    }

    public async Task<IngestReturnTrackingResult> Handle(IngestReturnTrackingEventCommand request, CancellationToken cancellationToken)
    {
        return await _returnTrackingService.IngestReturnTrackingEventAsync(request.Request, cancellationToken);
    }
}

