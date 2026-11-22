using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record ConfirmPackageDeliveryCommand(ConfirmPackageDeliveryRequest Request) : IRequest<DeliveryConfirmationResult>;

public class ConfirmPackageDeliveryCommandHandler : IRequestHandler<ConfirmPackageDeliveryCommand, DeliveryConfirmationResult>
{
    private readonly IDeliveryConfirmationService _deliveryConfirmationService;
    private readonly WarehouseOperationalAuthorization _warehouseAuth;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public ConfirmPackageDeliveryCommandHandler(
        IDeliveryConfirmationService deliveryConfirmationService,
        WarehouseOperationalAuthorization warehouseAuth,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _deliveryConfirmationService = deliveryConfirmationService;
        _warehouseAuth = warehouseAuth;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<DeliveryConfirmationResult> Handle(ConfirmPackageDeliveryCommand command, CancellationToken cancellationToken)
    {
        var authorizedPackage = await _warehouseAuth.ValidateCourierPackageAccessByIdAsync(command.Request.PackageId, cancellationToken);
        OutboundOperationalAuthorization.BindValidatedCourierTenant(_tenantProvider, _currentUser, authorizedPackage.ClientId);
        command.Request.TenantIdOverride = null;
        command.Request.BeforeCommitCustodyValidationAsync = async ct =>
            await _warehouseAuth.ValidateCourierPackageAccessByIdAsync(command.Request.PackageId, ct);

        return await _deliveryConfirmationService.ConfirmDeliveryAsync(command.Request, cancellationToken);
    }
}

