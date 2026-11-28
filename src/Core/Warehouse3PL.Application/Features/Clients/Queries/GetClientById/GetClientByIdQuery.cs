using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Queries.GetClientById;

public record GetClientByIdQuery(Guid Id) : IRequest<ClientDto>;

public class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetClientByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<ClientDto> Handle(GetClientByIdQuery request, CancellationToken cancellationToken)
    {
        var (_, authorizedClientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.Id,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.View]);

        var client = await _context.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Users)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == authorizedClientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Client), authorizedClientId);

        return client.Adapt<ClientDto>();
    }
}
