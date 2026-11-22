using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record RecordCustomerContactCommand(RecordCustomerContactRequest Request) : IRequest<DeliveryExceptionResult>;

public class RecordCustomerContactCommandHandler : IRequestHandler<RecordCustomerContactCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public RecordCustomerContactCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<DeliveryExceptionResult> Handle(RecordCustomerContactCommand command, CancellationToken cancellationToken)
    {
        await _outboundAuth.ValidateStaffExceptionMutationAsync(
            command.Request.ExceptionId,
            nameof(RecordCustomerContactCommand),
            cancellationToken);

        command.Request.TenantIdOverride = null;
        if (_currentUser.UserId.HasValue)
        {
            command.Request.StaffUserId = _currentUser.UserId.Value;
        }

        return await _deliveryExceptionService.RecordCustomerContactAsync(command.Request, cancellationToken);
    }
}

