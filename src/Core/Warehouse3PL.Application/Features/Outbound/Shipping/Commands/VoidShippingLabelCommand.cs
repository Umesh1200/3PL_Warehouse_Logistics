using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Commands;

public record VoidShippingLabelCommand(
    Guid LabelId,
    string Reason,
    string? IdempotencyKey = null,
    bool SimulateFailure = false) : IRequest<VoidShippingLabelResult>;

public class VoidShippingLabelCommandHandler : IRequestHandler<VoidShippingLabelCommand, VoidShippingLabelResult>
{
    private readonly IShippingLabelService _shippingLabelService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public VoidShippingLabelCommandHandler(
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

    public async Task<VoidShippingLabelResult> Handle(
        VoidShippingLabelCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(VoidShippingLabelCommand));

        var label = await _context.ShippingLabels
            .AsNoTracking()
            .Where(l => l.Id == command.LabelId)
            .Select(l => new { l.Id, FacilityId = l.Package.FacilityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (label == null)
        {
            throw new EntityNotFoundException(nameof(ShippingLabel), command.LabelId);
        }

        _outboundAuth.ValidateStaffMutationFacilityAccess(label.FacilityId, nameof(VoidShippingLabelCommand));

        return await _shippingLabelService.VoidLabelAsync(new VoidShippingLabelRequest
        {
            LabelId = command.LabelId,
            Reason = command.Reason,
            IdempotencyKey = command.IdempotencyKey,
            SimulateFailure = command.SimulateFailure,
            UserId = _currentUser.UserId ?? Guid.Empty
        }, cancellationToken);
    }
}

