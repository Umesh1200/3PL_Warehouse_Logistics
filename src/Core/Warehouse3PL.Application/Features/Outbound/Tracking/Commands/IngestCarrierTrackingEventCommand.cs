using MediatR;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record IngestCarrierTrackingEventCommand(
    string CarrierCode,
    string TrackingNumber,
    string CarrierEventCode,
    string CarrierDescription,
    DateTime OccurredAtUtc,
    string? CarrierEventId = null,
    string? EventCity = null,
    string? EventStateOrProvince = null,
    string? EventPostalCode = null,
    string? EventCountry = null,
    string? RawPayloadJson = null) : IRequest<IngestTrackingEventResult>;

public class IngestCarrierTrackingEventCommandHandler : IRequestHandler<IngestCarrierTrackingEventCommand, IngestTrackingEventResult>
{
    private readonly IShipmentTrackingService _trackingService;
    private readonly ITenantProvider _tenantProvider;

    public IngestCarrierTrackingEventCommandHandler(
        IShipmentTrackingService trackingService,
        ITenantProvider tenantProvider)
    {
        _trackingService = trackingService;
        _tenantProvider = tenantProvider;
    }

    public async Task<IngestTrackingEventResult> Handle(
        IngestCarrierTrackingEventCommand command,
        CancellationToken cancellationToken)
    {
        return await _trackingService.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = command.CarrierCode,
            TrackingNumber = command.TrackingNumber,
            CarrierEventId = command.CarrierEventId,
            CarrierEventCode = command.CarrierEventCode,
            CarrierDescription = command.CarrierDescription,
            OccurredAtUtc = command.OccurredAtUtc,
            EventCity = command.EventCity,
            EventStateOrProvince = command.EventStateOrProvince,
            EventPostalCode = command.EventPostalCode,
            EventCountry = command.EventCountry,
            RawPayloadJson = command.RawPayloadJson,
            TenantIdOverride = _tenantProvider.BypassTenantFilter ? null : _tenantProvider.TenantId
        }, cancellationToken);
    }
}

