using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Queries.GetClientById;

public record GetClientByIdQuery(Guid Id) : IRequest<ClientDto>;

public class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetClientByIdQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<ClientDto> Handle(GetClientByIdQuery request, CancellationToken cancellationToken)
    {
        // Enforce tenant boundary for non-staff; fail-closed without tenant
        if (!_currentUser.IsStaff)
        {
            if (!_currentUser.TenantId.HasValue)
            {
                throw new ForbiddenException("Non-staff users without an assigned tenant context are forbidden from accessing client records.");
            }
            if (_currentUser.TenantId.Value != request.Id)
            {
                throw new ForbiddenException("You cannot view details for another client tenant.");
            }
        }

        var client = await _context.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Users)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken);

        if (client == null)
        {
            throw new EntityNotFoundException(nameof(Client), request.Id);
        }

        return client.Adapt<ClientDto>();
    }
}

