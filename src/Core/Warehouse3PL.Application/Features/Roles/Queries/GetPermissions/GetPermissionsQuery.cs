using MediatR;
using Warehouse3PL.Domain.Constants;

namespace Warehouse3PL.Application.Features.Roles.Queries.GetPermissions;

public record GetPermissionsQuery : IRequest<IReadOnlyList<string>>;

public class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetPermissionsQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Permissions.GetAllPermissions());
    }
}

