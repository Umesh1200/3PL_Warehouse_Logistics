using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;

public record DeactivateClientCommand(Guid Id) : IRequest<bool>;

public class DeactivateClientCommandHandler : IRequestHandler<DeactivateClientCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public DeactivateClientCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeactivateClientCommand request, CancellationToken cancellationToken)
    {
        // Enforce staff-only authorization policy for client deactivation
        if (!_currentUser.IsStaff)
        {
            throw new ForbiddenException("Only authorized staff members are permitted to deactivate client tenants.");
        }

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken);

        if (client == null)
        {
            throw new EntityNotFoundException(nameof(Client), request.Id);
        }

        client.Status = ClientStatus.Suspended;
        client.IsDeleted = true;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

