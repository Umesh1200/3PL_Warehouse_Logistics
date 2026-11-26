using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Reference.Queries;

internal sealed record CalendarCallerScope(
    UserType UserType,
    bool IsSuperAdmin,
    bool HasMultiFacilityRead,
    Guid? VerifiedStaffFacilityId,
    IReadOnlyList<Guid> AuthorizedClientFacilityIds);

internal static class CalendarAccessHelper
{
    public static async Task<CalendarCallerScope> ResolveCallerScopeAsync(
        IApplicationDbContext db,
        ICurrentUserContext currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue || currentUser.UserId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Authentication required.");
        }

        var userId = currentUser.UserId.Value;
        var dbUser = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                u.ClientId,
                u.AssignedFacilityId,
                RoleNames = u.UserRoles.Select(ur => ur.Role.RoleName).ToList(),
                Permissions = u.UserRoles.SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.PermissionKey)).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (dbUser == null || dbUser.IsDeleted || dbUser.Status != UserStatus.Active)
        {
            throw new ForbiddenException("Caller account is inactive, deleted, or unverified.");
        }

        if (currentUser.UserType != dbUser.UserType)
        {
            throw new ForbiddenException("Caller account type does not match authoritative database identity.");
        }

        if (dbUser.UserType == UserType.Staff)
        {
            var isSuperAdmin = dbUser.RoleNames.Contains(SystemRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase);
            var hasMultiFacilityRead = isSuperAdmin ||
                (dbUser.Permissions.Contains(Permissions.Analytics.MultiFacilityView, StringComparer.OrdinalIgnoreCase) &&
                 currentUser.HasPermission(Permissions.Analytics.MultiFacilityView));

            return new CalendarCallerScope(
                UserType.Staff,
                isSuperAdmin,
                hasMultiFacilityRead,
                dbUser.AssignedFacilityId,
                []);
        }

        if (dbUser.UserType == UserType.Client && dbUser.ClientId.HasValue)
        {
            var authorizedIds = await db.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == dbUser.ClientId.Value && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId)
                .Distinct()
                .ToListAsync(ct);

            return new CalendarCallerScope(
                UserType.Client,
                false,
                false,
                null,
                authorizedIds);
        }

        return new CalendarCallerScope(dbUser.UserType, false, false, null, []);
    }

    public static async Task RequireFacilityReadAccessAsync(
        IApplicationDbContext db,
        ICurrentUserContext currentUser,
        Guid facilityId,
        CancellationToken ct)
    {
        var scope = await ResolveCallerScopeAsync(db, currentUser, ct);

        if (scope.IsSuperAdmin || scope.HasMultiFacilityRead)
        {
            return;
        }

        if (scope.UserType == UserType.Staff)
        {
            if (scope.VerifiedStaffFacilityId.HasValue && scope.VerifiedStaffFacilityId.Value == facilityId)
            {
                return;
            }

            throw new ForbiddenException($"Staff caller is not authorized to view calendars for facility '{facilityId}'.");
        }

        if (scope.UserType == UserType.Client && scope.AuthorizedClientFacilityIds.Contains(facilityId))
        {
            return;
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

        var scope = request.FacilityId.HasValue
            ? null
            : await CalendarAccessHelper.ResolveCallerScopeAsync(_db, _currentUser, cancellationToken);

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
        else if (scope != null && !scope.IsSuperAdmin && !scope.HasMultiFacilityRead)
        {
            // Restrict Facility-scoped holidays to strictly verified facility assignments / client access;
            // National and Regional reference holidays remain readable across authenticated callers.
            if (scope.UserType == UserType.Staff && scope.VerifiedStaffFacilityId.HasValue)
            {
                var staffFacilityId = scope.VerifiedStaffFacilityId.Value;
                query = query.Where(h => h.HolidayScope != HolidayScope.Facility || h.FacilityId == staffFacilityId);
            }
            else if (scope.UserType == UserType.Client && scope.AuthorizedClientFacilityIds.Count > 0)
            {
                var authorizedIds = scope.AuthorizedClientFacilityIds;
                query = query.Where(h => h.HolidayScope != HolidayScope.Facility ||
                                         (h.FacilityId.HasValue && authorizedIds.Contains(h.FacilityId.Value)));
            }
            else
            {
                // Unassigned Staff (without MultiFacilityView), Drivers, and Clients with zero active facility grants
                // see only National and Regional holidays (never other facilities' Facility-scoped holidays).
                query = query.Where(h => h.HolidayScope != HolidayScope.Facility);
            }
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
            if (instant.Kind == DateTimeKind.Unspecified)
            {
                throw new DomainException("UtcInstant must explicitly specify UTC or offset kind.", "INVALID_TIMESTAMP_KIND");
            }
            var normalizedUtc = instant.Kind == DateTimeKind.Utc ? instant : instant.ToUniversalTime();
            TimeZoneInfo tz;
            if (string.IsNullOrWhiteSpace(facTzId))
            {
                tz = TimeZoneInfo.Utc;
            }
            else
            {
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(facTzId.Trim());
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    throw new DomainException($"Configured facility timezone '{facTzId}' is invalid.", "INVALID_TIMEZONE");
                }
            }
            targetDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, tz));
        }

        return await _workingCalendar.EvaluateWorkingDayAsync(request.FacilityId, targetDate, cancellationToken);
    }
}
