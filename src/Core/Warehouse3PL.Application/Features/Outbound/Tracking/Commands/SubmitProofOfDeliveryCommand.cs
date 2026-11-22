using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SubmitProofOfDeliveryCommand(SubmitProofOfDeliveryRequest Request) : IRequest<SubmitProofOfDeliveryResult>;

public class SubmitProofOfDeliveryCommandHandler : IRequestHandler<SubmitProofOfDeliveryCommand, SubmitProofOfDeliveryResult>
{
    private readonly IProofOfDeliveryService _podService;
    private readonly WarehouseOperationalAuthorization _warehouseAuth;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public SubmitProofOfDeliveryCommandHandler(
        IProofOfDeliveryService podService,
        WarehouseOperationalAuthorization warehouseAuth,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _podService = podService;
        _warehouseAuth = warehouseAuth;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<SubmitProofOfDeliveryResult> Handle(SubmitProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        // Authorize BEFORE any staged evidence upload executes in ProofOfDeliveryService
        var authorizedPackage = await _warehouseAuth.ValidateCourierPackageAccessByIdAsync(command.Request.PackageId, cancellationToken);
        OutboundOperationalAuthorization.BindValidatedCourierTenant(_tenantProvider, _currentUser, authorizedPackage.ClientId);
        command.Request.TenantIdOverride = null;

        return await _podService.SubmitProofOfDeliveryAsync(command.Request, cancellationToken);
    }
}

