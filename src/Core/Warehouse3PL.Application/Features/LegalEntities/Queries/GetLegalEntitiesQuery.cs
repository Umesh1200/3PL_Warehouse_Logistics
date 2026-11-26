using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;

namespace Warehouse3PL.Application.Features.LegalEntities.Queries;

public sealed record GetLegalEntitiesQuery(
    string? CountryCode = null,
    bool? IsActive = null) : IRequest<IReadOnlyList<LegalEntityDto>>;

public sealed class GetLegalEntitiesQueryHandler
    : IRequestHandler<GetLegalEntitiesQuery, IReadOnlyList<LegalEntityDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public GetLegalEntitiesQueryHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<IReadOnlyList<LegalEntityDto>> Handle(
        GetLegalEntitiesQuery request,
        CancellationToken cancellationToken)
    {
        await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ViewLegalEntities,
            Permissions.Global.ManageLegalEntities);

        var query = _db.LegalEntities.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            var cc = request.CountryCode.Trim().ToUpperInvariant();
            query = query.Where(le => le.CountryCode == cc);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(le => le.IsActive == request.IsActive.Value);
        }

        var rows = await query
            .OrderBy(le => le.LegalCode)
            .ToListAsync(cancellationToken);

        return rows.Select(le => le.ToDto()).ToList();
    }
}

public sealed record GetLegalEntityByIdQuery(Guid Id) : IRequest<LegalEntityDto?>;

public sealed class GetLegalEntityByIdQueryHandler
    : IRequestHandler<GetLegalEntityByIdQuery, LegalEntityDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly IConfigurationConsistencyService? _consistencyService;

    public GetLegalEntityByIdQueryHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        IConfigurationConsistencyService? consistencyService = null)
    {
        _db = db;
        _auth = auth;
        _consistencyService = consistencyService;
    }

    public async Task<LegalEntityDto?> Handle(
        GetLegalEntityByIdQuery request,
        CancellationToken cancellationToken)
    {
        await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ViewLegalEntities,
            Permissions.Global.ManageLegalEntities);

        var entity = _consistencyService != null
            ? await _consistencyService.GetVerifiedLegalEntityAsync(request.Id, cancellationToken)
            : await _db.LegalEntities
                .AsNoTracking()
                .FirstOrDefaultAsync(le => le.Id == request.Id, cancellationToken);

        return entity?.ToDto();
    }
}
