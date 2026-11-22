using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ReportDriverDeliveryExceptionCommand(ReportDriverExceptionRequest Request) : IRequest<DeliveryExceptionResult>;

public class ReportDriverDeliveryExceptionCommandHandler : IRequestHandler<ReportDriverDeliveryExceptionCommand, DeliveryExceptionResult>
{
    private readonly IDeliveryExceptionService _deliveryExceptionService;
    private readonly WarehouseOperationalAuthorization _warehouseAuth;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public ReportDriverDeliveryExceptionCommandHandler(
        IDeliveryExceptionService deliveryExceptionService,
        WarehouseOperationalAuthorization warehouseAuth,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _deliveryExceptionService = deliveryExceptionService;
        _warehouseAuth = warehouseAuth;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<DeliveryExceptionResult> Handle(ReportDriverDeliveryExceptionCommand command, CancellationToken cancellationToken)
    {
        var authorizedPackage = await _warehouseAuth.ValidateCourierPackageAccessByIdAsync(command.Request.PackageId, cancellationToken);
        OutboundOperationalAuthorization.BindValidatedCourierTenant(_tenantProvider, _currentUser, authorizedPackage.ClientId);
        command.Request.TenantIdOverride = null;
        command.Request.DriverUserId = _currentUser.UserId;

        return await _deliveryExceptionService.ReportDriverExceptionAsync(command.Request, cancellationToken);
    }
}

