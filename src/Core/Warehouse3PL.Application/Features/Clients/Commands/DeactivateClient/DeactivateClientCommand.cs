using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Mapster;

namespace Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;

public record DeactivateClientCommand(Guid Id) : IRequest<bool>;

public record TransitionClientStatusCommand(Guid Id, ClientStatus TargetStatus, int? ExpectedConfigRevision = null)
    : IRequest<ClientDto>;

public class DeactivateClientCommandHandler : IRequestHandler<DeactivateClientCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public DeactivateClientCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<bool> Handle(DeactivateClientCommand request, CancellationToken cancellationToken)
    {
        await _auth.ResolveAuthorizedClientScopeAsync(
            request.Id,
            allowClientSelf: false,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.Delete]);

        return await _context.ExecuteWithClientLifecycleLockAsync(request.Id, async () =>
        {
            var client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Client), request.Id);

            await PartnerMasterDependencyGuard.EnsureClientCanBeDeactivatedAsync(
                _context, client.Id, client.AccountCode, ClientStatus.Suspended, cancellationToken);

            client.Status = ClientStatus.Suspended;
            client.IsDeleted = true;
            client.DeletedAt = DateTime.UtcNow;
            client.ConfigRevision++;

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}

public class TransitionClientStatusCommandHandler : IRequestHandler<TransitionClientStatusCommand, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public TransitionClientStatusCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientDto> Handle(TransitionClientStatusCommand request, CancellationToken cancellationToken)
    {
        var requiredPerms = request.TargetStatus == ClientStatus.Terminated
            ? new[] { Permissions.Clients.Delete, Permissions.Clients.Edit }
            : new[] { Permissions.Clients.Edit };

        await _auth.ResolveAuthorizedClientScopeAsync(
            request.Id,
            allowClientSelf: false,
            cancellationToken,
            allowedStaffPermissions: requiredPerms);

        return await _context.ExecuteWithClientLifecycleLockAsync(request.Id, async () =>
        {
            var client = await _context.Clients
                .Include(c => c.Contacts)
                .Include(c => c.Users)
                .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Client), request.Id);

            if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != client.ConfigRevision)
            {
                throw new ConcurrencyException(
                    "CLIENT_CONCURRENCY_CONFLICT",
                    $"Client '{client.AccountCode}' was modified concurrently.");
            }

            if (client.Status == ClientStatus.Terminated && request.TargetStatus != ClientStatus.Terminated)
            {
                throw new DomainException(
                    $"Client '{client.AccountCode}' is Terminated and cannot be reactivated.",
                    "CLIENT_TERMINATED_IMMUTABLE");
            }

            if ((request.TargetStatus == ClientStatus.Suspended || request.TargetStatus == ClientStatus.Terminated) &&
                client.Status != request.TargetStatus)
            {
                await PartnerMasterDependencyGuard.EnsureClientCanBeDeactivatedAsync(
                    _context, client.Id, client.AccountCode, request.TargetStatus, cancellationToken);
            }

            client.Status = request.TargetStatus;
            client.ConfigRevision++;
            await _context.SaveChangesAsync(cancellationToken);

            return client.Adapt<ClientDto>();
        }, cancellationToken);
    }
}
