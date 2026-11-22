using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record CreateShipmentCommand(
    Guid ClientId,
    Guid FacilityId,
    string CarrierCode,
    CarrierServiceLevel ServiceLevel = CarrierServiceLevel.StandardGround,
    Guid? StagingLocationId = null) : IRequest<CreateShipmentResult>;

public class CreateShipmentCommandHandler : IRequestHandler<CreateShipmentCommand, CreateShipmentResult>
{
    private readonly IShipmentConsolidationService _consolidationService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public CreateShipmentCommandHandler(
        IShipmentConsolidationService consolidationService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _consolidationService = consolidationService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<CreateShipmentResult> Handle(
        CreateShipmentCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.ValidateStaffMutationFacilityAccess(command.FacilityId, nameof(CreateShipmentCommand));

        return await _consolidationService.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = command.ClientId,
            FacilityId = command.FacilityId,
            CarrierCode = command.CarrierCode,
            ServiceLevel = command.ServiceLevel,
            StagingLocationId = command.StagingLocationId,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

