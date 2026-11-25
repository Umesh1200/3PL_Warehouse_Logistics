using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Reference.Queries;

public sealed record GetTimeZonesQuery(
    string? CountryCode = null,
    DateTime? AtUtc = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<TimeZoneProfileDto>>;

public sealed class GetTimeZonesQueryHandler : IRequestHandler<GetTimeZonesQuery, IReadOnlyList<TimeZoneProfileDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ITimeZoneOffsetResolver _offsetResolver;
    private readonly IDateTimeProvider _dateTime;

    public GetTimeZonesQueryHandler(
        IApplicationDbContext db,
        ITimeZoneOffsetResolver offsetResolver,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _offsetResolver = offsetResolver;
        _dateTime = dateTime;
    }

    public async Task<IReadOnlyList<TimeZoneProfileDto>> Handle(GetTimeZonesQuery request, CancellationToken cancellationToken)
    {
        var evaluatedAtUtc = request.AtUtc.HasValue
            ? (request.AtUtc.Value.Kind == DateTimeKind.Utc
                ? request.AtUtc.Value
                : DateTime.SpecifyKind(request.AtUtc.Value, DateTimeKind.Utc))
            : _dateTime.UtcNow;

        var query = _db.TimeZoneProfiles.AsNoTracking();
        if (request.ActiveOnly)
        {
            query = query.Where(tz => tz.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            var code = request.CountryCode.Trim().ToUpperInvariant();
            query = query.Where(tz => tz.CountryCode == code);
        }

        var rows = await query
            .OrderBy(tz => tz.TimeZoneId)
            .ToListAsync(cancellationToken);

        return rows.Select(tz => new TimeZoneProfileDto(
            tz.TimeZoneId,
            tz.DisplayName,
            tz.ObservesDst,
            tz.CountryCode,
            tz.IsActive,
            _offsetResolver.GetUtcOffsetMinutes(tz.TimeZoneId, evaluatedAtUtc),
            evaluatedAtUtc)).ToList();
    }
}
