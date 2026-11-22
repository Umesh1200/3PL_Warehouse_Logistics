using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

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
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GenerateShippingLabelCommandHandler(
        IShippingLabelService shippingLabelService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _shippingLabelService = shippingLabelService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<GenerateShippingLabelResult> Handle(
        GenerateShippingLabelCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GenerateShippingLabelCommand));

        var package = await _context.Packages
            .AsNoTracking()
            .Where(p => p.Id == command.PackageId && !p.IsDeleted)
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), command.PackageId);
        }

        _outboundAuth.ValidateStaffMutationFacilityAccess(package.FacilityId, nameof(GenerateShippingLabelCommand));

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

