using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Reference.Queries;

internal static class CalendarAccessHelper
{
    public static async Task RequireFacilityReadAccessAsync(
        IApplicationDbContext db,
        ICurrentUserContext currentUser,
        Guid facilityId,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue || currentUser.UserId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authentication required.");
        }

        if (currentUser.IsSuperAdmin)
        {
            return;
        }

        if (currentUser.IsStaff)
        {
            if (currentUser.FacilityId.HasValue && currentUser.FacilityId.Value != facilityId)
            {
                throw new ForbiddenException($"Staff caller is not authorized for facility '{facilityId}'.");
            }
            return;
        }

        if (currentUser.UserType == UserType.Client && currentUser.TenantId.HasValue)
        {
            var hasAccess = await db.ClientFacilityAccesses
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == currentUser.TenantId.Value &&
                               a.FacilityId == facilityId &&
                               a.Status == FacilityAccessStatus.Active, ct);
            if (hasAccess)
            {
                return;
            }
        }

        throw new ForbiddenException($"Caller is not authorized to view calendars for facility '{facilityId}'.");
    }
}

public sealed record GetBikramSambatMonthsQuery(int? BsYear = null)
    : IRequest<IReadOnlyList<BikramSambatCalendarMonthDto>>;

public sealed class GetBikramSambatMonthsQueryHandler
    : IRequestHandler<GetBikramSambatMonthsQuery, IReadOnlyList<BikramSambatCalendarMonthDto>>
{
    private readonly IBikramSambatCalendarService _bsService;

    public GetBikramSambatMonthsQueryHandler(IBikramSambatCalendarService bsService)
    {
        _bsService = bsService;
    }

    public Task<IReadOnlyList<BikramSambatCalendarMonthDto>> Handle(
        GetBikramSambatMonthsQuery request,
        CancellationToken cancellationToken)
        => _bsService.GetCalendarMonthsAsync(request.BsYear, cancellationToken);
}

public sealed record BikramSambatConversionResponse(
    BikramSambatDateResult? ToBs,
    DateOnly? ToGregorian,
    DateOnly? VatReturnDueDate);

public sealed record ConvertBikramSambatDateQuery(
    DateOnly? GregorianDate = null,
    DateTime? UtcInstant = null,
    string? TimeZoneId = null,
    int? BsYear = null,
    int? BsMonth = null,
    int? BsDay = null,
    int? VatForBsYear = null,
    int? VatForBsMonth = null) : IRequest<BikramSambatConversionResponse>;

public sealed class ConvertBikramSambatDateQueryHandler
    : IRequestHandler<ConvertBikramSambatDateQuery, BikramSambatConversionResponse>
{
    private readonly IBikramSambatCalendarService _bsService;

    public ConvertBikramSambatDateQueryHandler(IBikramSambatCalendarService bsService)
    {
        _bsService = bsService;
    }

    public async Task<BikramSambatConversionResponse> Handle(
        ConvertBikramSambatDateQuery request,
        CancellationToken cancellationToken)
    {
        BikramSambatDateResult? toBs = null;
        DateOnly? toAd = null;
        DateOnly? vatDue = null;

        if (request.UtcInstant.HasValue)
        {
            if (string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                throw new DomainException("TimeZoneId is required when converting a UTC instant.", "INVALID_TIMEZONE");
            }

            var normalizedUtc = request.UtcInstant.Value.Kind == DateTimeKind.Unspecified
                ? throw new DomainException("UtcInstant must explicitly use DateTimeKind.Utc or offset kind.", "BS_UNSPECIFIED_TIMESTAMP_KIND")
                : request.UtcInstant.Value;

            toBs = await _bsService.ConvertInstantInTimeZoneToBikramSambatAsync(
                normalizedUtc,
                request.TimeZoneId,
                cancellationToken);
            toAd = toBs.GregorianDate;
        }
        else if (request.GregorianDate.HasValue)
        {
            toBs = await _bsService.ConvertToBikramSambatAsync(request.GregorianDate.Value, cancellationToken);
            toAd = toBs.GregorianDate;
        }
        else if (request.BsYear.HasValue && request.BsMonth.HasValue && request.BsDay.HasValue)
        {
            toAd = await _bsService.ConvertToGregorianAsync(
                request.BsYear.Value,
                request.BsMonth.Value,
                request.BsDay.Value,
                cancellationToken);
            toBs = await _bsService.ConvertToBikramSambatAsync(toAd.Value, cancellationToken);
        }
        else if (!request.VatForBsYear.HasValue || !request.VatForBsMonth.HasValue)
        {
            throw new DomainException(
                "Supply GregorianDate, UtcInstant + TimeZoneId, BsYear/BsMonth/BsDay, or VatForBsYear/VatForBsMonth.",
                "BS_CONVERSION_INPUT_REQUIRED");
        }

        if (request.VatForBsYear.HasValue && request.VatForBsMonth.HasValue)
        {
            vatDue = await _bsService.ResolveVatReturnDueDateAsync(
                request.VatForBsYear.Value,
                request.VatForBsMonth.Value,
                cancellationToken);
        }

        return new BikramSambatConversionResponse(toBs, toAd, vatDue);
    }
}

public sealed record GetHolidayCalendarsQuery(
    string? CountryCode = null,
    string? SubdivisionCode = null,
    Guid? FacilityId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool ActiveOnly = true) : IRequest<IReadOnlyList<HolidayCalendarDto>>;

public sealed class GetHolidayCalendarsQueryHandler
    : IRequestHandler<GetHolidayCalendarsQuery, IReadOnlyList<HolidayCalendarDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetHolidayCalendarsQueryHandler(IApplicationDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<HolidayCalendarDto>> Handle(
        GetHolidayCalendarsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FacilityId.HasValue)
        {
            await CalendarAccessHelper.RequireFacilityReadAccessAsync(_db, _currentUser, request.FacilityId.Value, cancellationToken);
        }

        var query = _db.HolidayCalendars.AsNoTracking();

        if (request.ActiveOnly)
        {
            query = query.Where(h => h.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            var cc = request.CountryCode.Trim().ToUpperInvariant();
            query = query.Where(h => h.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(request.SubdivisionCode))
        {
            var sub = request.SubdivisionCode.Trim().ToUpperInvariant();
            query = query.Where(h => h.SubdivisionCode == sub);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(h => h.FacilityId == request.FacilityId.Value || h.FacilityId == null);
        }
        else if (!_currentUser.IsSuperAdmin && !_currentUser.IsStaff)
        {
            // Non-staff without explicit FacilityId only sees National/Regional holidays + their authorized facilities
            var authorizedIds = _currentUser.UserType == UserType.Client && _currentUser.TenantId.HasValue
                ? await _db.ClientFacilityAccesses
                    .AsNoTracking()
                    .Where(a => a.ClientId == _currentUser.TenantId.Value && a.Status == FacilityAccessStatus.Active)
                    .Select(a => a.FacilityId)
                    .ToListAsync(cancellationToken)
                : new List<Guid>();

            query = query.Where(h => h.HolidayScope != HolidayScope.Facility ||
                                     (h.FacilityId.HasValue && authorizedIds.Contains(h.FacilityId.Value)));
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(h => h.HolidayDate >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(h => h.HolidayDate <= request.ToDate.Value);
        }

        var rows = await query
            .OrderBy(h => h.HolidayDate)
            .ThenByDescending(h => (int)h.HolidayScope)
            .ToListAsync(cancellationToken);

        return rows.Select(h => h.ToDto()).ToList();
    }
}

public sealed record GetFacilityCalendarsQuery(Guid FacilityId, bool ActiveOnly = true)
    : IRequest<IReadOnlyList<FacilityCalendarDto>>;

public sealed class GetFacilityCalendarsQueryHandler
    : IRequestHandler<GetFacilityCalendarsQuery, IReadOnlyList<FacilityCalendarDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GetFacilityCalendarsQueryHandler(IApplicationDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<FacilityCalendarDto>> Handle(
        GetFacilityCalendarsQuery request,
        CancellationToken cancellationToken)
    {
        await CalendarAccessHelper.RequireFacilityReadAccessAsync(_db, _currentUser, request.FacilityId, cancellationToken);

        var query = _db.FacilityCalendars
            .AsNoTracking()
            .Where(fc => fc.FacilityId == request.FacilityId);

        if (request.ActiveOnly)
        {
            query = query.Where(fc => fc.IsActive);
        }

        var rows = await query
            .OrderByDescending(fc => fc.EffectiveFromUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(fc => fc.ToDto()).ToList();
    }
}

public sealed record EvaluateFacilityWorkingDayQuery(
    Guid FacilityId,
    DateOnly? LocalDate = null,
    DateTime? UtcInstant = null) : IRequest<WorkingDayEvaluationResult>;

public sealed class EvaluateFacilityWorkingDayQueryHandler
    : IRequestHandler<EvaluateFacilityWorkingDayQuery, WorkingDayEvaluationResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IWorkingCalendarService _workingCalendar;
    private readonly IDateTimeProvider _dateTime;

    public EvaluateFacilityWorkingDayQueryHandler(
        IApplicationDbContext db,
        ICurrentUserContext currentUser,
        IWorkingCalendarService workingCalendar,
        IDateTimeProvider dateTime)
    {
        _db = db;
        _currentUser = currentUser;
        _workingCalendar = workingCalendar;
        _dateTime = dateTime;
    }

    public async Task<WorkingDayEvaluationResult> Handle(
        EvaluateFacilityWorkingDayQuery request,
        CancellationToken cancellationToken)
    {
        await CalendarAccessHelper.RequireFacilityReadAccessAsync(_db, _currentUser, request.FacilityId, cancellationToken);

        DateOnly targetDate;
        if (request.LocalDate.HasValue)
        {
            targetDate = request.LocalDate.Value;
        }
        else
        {
            var facTzId = await _db.Facilities
                .AsNoTracking()
                .Where(f => f.Id == request.FacilityId)
                .Select(f => f.Timezone)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException("Facility", request.FacilityId);

            var instant = request.UtcInstant ?? _dateTime.UtcNow;
            var normalizedUtc = instant.Kind == DateTimeKind.Utc ? instant : DateTime.SpecifyKind(instant, DateTimeKind.Utc);
            var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(facTzId) ? "UTC" : facTzId);
            targetDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, tz));
        }

        return await _workingCalendar.EvaluateWorkingDayAsync(request.FacilityId, targetDate, cancellationToken);
    }
}
