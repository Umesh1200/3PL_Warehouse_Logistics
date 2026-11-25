using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetCountryDivisionsQuery(
    string CountryCode,
    AdministrativeDivisionLevel? Level = null,
    Guid? ParentDivisionId = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<AdministrativeDivisionDto>?>;

public sealed class GetCountryDivisionsQueryHandler
    : IRequestHandler<GetCountryDivisionsQuery, IReadOnlyList<AdministrativeDivisionDto>?>
{
    private readonly IApplicationDbContext _db;

    public GetCountryDivisionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdministrativeDivisionDto>?> Handle(
        GetCountryDivisionsQuery request,
        CancellationToken cancellationToken)
    {
        var code = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        var countryExists = await _db.CountryProfiles
            .AsNoTracking()
            .AnyAsync(c => c.CountryCode == code, cancellationToken);

        if (!countryExists)
        {
            return null;
        }

        var query = _db.AdministrativeDivisions
            .AsNoTracking()
            .Where(d => d.CountryCode == code);

        if (request.ActiveOnly)
        {
            query = query.Where(d => d.IsActive);
        }

        if (request.Level.HasValue)
        {
            query = query.Where(d => d.DivisionLevel == request.Level.Value);
        }

        if (request.ParentDivisionId.HasValue)
        {
            query = query.Where(d => d.ParentDivisionId == request.ParentDivisionId.Value);
        }

        var rows = await query
            .OrderBy(d => d.DivisionLevel)
            .ThenBy(d => d.Code)
            .ToListAsync(cancellationToken);

        return rows.Select(d => d.ToDto()).ToList();
    }
}
