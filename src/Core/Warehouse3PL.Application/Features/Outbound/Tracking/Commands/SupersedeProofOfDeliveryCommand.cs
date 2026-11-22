using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Tracking.Commands;

public record SupersedeProofOfDeliveryCommand(SupersedeProofOfDeliveryRequest Request) : IRequest<SubmitProofOfDeliveryResult>;

public class SupersedeProofOfDeliveryCommandHandler : IRequestHandler<SupersedeProofOfDeliveryCommand, SubmitProofOfDeliveryResult>
{
    private readonly IProofOfDeliveryService _podService;
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public SupersedeProofOfDeliveryCommandHandler(
        IProofOfDeliveryService podService,
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _podService = podService;
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<SubmitProofOfDeliveryResult> Handle(SupersedeProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(SupersedeProofOfDeliveryCommand));

        var existingPod = await _context.ProofOfDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == command.Request.ExistingPodId)
            .Select(p => new { p.Id, p.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ProofOfDelivery), command.Request.ExistingPodId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(existingPod.FacilityId, nameof(SupersedeProofOfDeliveryCommand));
        if (command.Request.ReplacementSubmission != null)
        {
            command.Request.ReplacementSubmission.TenantIdOverride = null;
        }

        return await _podService.SupersedeProofOfDeliveryAsync(command.Request, cancellationToken);
    }
}

