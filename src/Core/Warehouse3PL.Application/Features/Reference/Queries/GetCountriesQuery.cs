using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetCountriesQuery(bool ActiveOnly = true) : IRequest<IReadOnlyList<CountryProfileDto>>;

public sealed class GetCountriesQueryHandler : IRequestHandler<GetCountriesQuery, IReadOnlyList<CountryProfileDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCountriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<CountryProfileDto>> Handle(GetCountriesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.CountryProfiles.AsNoTracking();
        if (request.ActiveOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var rows = await query
            .OrderBy(c => c.CountryCode)
            .ToListAsync(cancellationToken);

        return rows.Select(c => c.ToDto()).ToList();
    }
}

public sealed record GetCountryByCodeQuery(string CountryCode) : IRequest<CountryProfileDto?>;

public sealed class GetCountryByCodeQueryHandler : IRequestHandler<GetCountryByCodeQuery, CountryProfileDto?>
{
    private readonly IApplicationDbContext _db;

    public GetCountryByCodeQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CountryProfileDto?> Handle(GetCountryByCodeQuery request, CancellationToken cancellationToken)
    {
        var code = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 2)
        {
            return null;
        }

        var entity = await _db.CountryProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryCode == code, cancellationToken);

        return entity?.ToDto();
    }
}
