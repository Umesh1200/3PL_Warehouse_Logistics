using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Reference.Commands;

public sealed record CreateHolidayCalendarCommand(
    string CountryCode,
    HolidayScope HolidayScope,
    DateOnly HolidayDate,
    string Name,
    string? SubdivisionCode = null,
    Guid? FacilityId = null,
    bool IsWorkingDayOverride = false) : IRequest<HolidayCalendarDto>;

public sealed class CreateHolidayCalendarCommandValidator : AbstractValidator<CreateHolidayCalendarCommand>
{
    public CreateHolidayCalendarCommandValidator()
    {
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.SubdivisionCode).MaximumLength(20);
    }
}

public sealed class CreateHolidayCalendarCommandHandler
    : IRequestHandler<CreateHolidayCalendarCommand, HolidayCalendarDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public CreateHolidayCalendarCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<HolidayCalendarDto> Handle(CreateHolidayCalendarCommand request, CancellationToken cancellationToken)
    {
        Guid staffUserId = request.HolidayScope == HolidayScope.Facility
            ? await _auth.RequireVerifiedFacilityManagerAsync(request.FacilityId, cancellationToken)
            : await _auth.RequireVerifiedStaffPermissionAsync(cancellationToken, Permissions.Global.ComplianceManage);

        var cc = request.CountryCode.Trim().ToUpperInvariant();
        var countryExists = await _db.CountryProfiles
            .AsNoTracking()
            .AnyAsync(c => c.CountryCode == cc && c.IsActive, cancellationToken);
        if (!countryExists)
        {
            throw new DomainException($"CountryCode '{cc}' is not an active CountryProfile.", "HOLIDAY_UNKNOWN_COUNTRY");
        }

        string? subCode = null;
        if (request.HolidayScope == HolidayScope.Regional)
        {
            subCode = (request.SubdivisionCode ?? string.Empty).Trim().ToUpperInvariant();
            var subExists = await _db.AdministrativeDivisions
                .AsNoTracking()
                .AnyAsync(d => d.CountryCode == cc && d.IsActive && (d.IsoCode == subCode || d.Code == subCode), cancellationToken);
            if (!subExists)
            {
                throw new DomainException(
                    $"SubdivisionCode '{subCode}' is not a valid active AdministrativeDivision under '{cc}'.",
                    "HOLIDAY_UNKNOWN_SUBDIVISION");
            }
        }

        if (request.HolidayScope == HolidayScope.Facility)
        {
            var facId = request.FacilityId!.Value;
            var fac = await _db.Facilities
                .AsNoTracking()
                .Where(f => f.Id == facId && !f.IsDeleted && f.IsActive)
                .Select(f => new { f.Id, f.CountryCode })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException("Facility", facId);

            var facCountry = fac.CountryCode?.Trim().ToUpperInvariant() switch
            {
                "USA" => "US",
                "NPL" => "NP",
                var x => x ?? string.Empty
            };

            if (!string.Equals(facCountry, cc, StringComparison.Ordinal))
            {
                throw new DomainException(
                    $"Facility '{facId}' belongs to country '{facCountry}', cannot attach '{cc}' facility holiday.",
                    "HOLIDAY_FACILITY_COUNTRY_MISMATCH");
            }
        }

        var duplicateExists = await _db.HolidayCalendars
            .AsNoTracking()
            .AnyAsync(h => h.IsActive &&
                           h.CountryCode == cc &&
                           h.HolidayScope == request.HolidayScope &&
                           h.SubdivisionCode == subCode &&
                           h.FacilityId == request.FacilityId &&
                           h.HolidayDate == request.HolidayDate,
                cancellationToken);
        if (duplicateExists)
        {
            throw new ConcurrencyException(
                "HOLIDAY_DUPLICATE_ACTIVE",
                $"An active {request.HolidayScope} holiday already exists on {request.HolidayDate:yyyy-MM-dd} for '{cc}'.");
        }

        var entity = HolidayCalendar.Create(
            cc,
            request.HolidayScope,
            request.HolidayDate,
            request.Name,
            subCode,
            request.FacilityId,
            request.IsWorkingDayOverride);
        entity.CreatedBy = staffUserId;

        _db.HolidayCalendars.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity.ToDto();
    }
}

public sealed record UpdateHolidayCalendarCommand(
    Guid HolidayId,
    string Name,
    bool IsWorkingDayOverride,
    bool IsActive,
    long ExpectedConfigRevision) : IRequest<HolidayCalendarDto>;

public sealed class UpdateHolidayCalendarCommandHandler
    : IRequestHandler<UpdateHolidayCalendarCommand, HolidayCalendarDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public UpdateHolidayCalendarCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<HolidayCalendarDto> Handle(UpdateHolidayCalendarCommand request, CancellationToken cancellationToken)
    {
        var existing = await _db.HolidayCalendars
            .FirstOrDefaultAsync(h => h.Id == request.HolidayId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(HolidayCalendar), request.HolidayId);

        Guid staffUserId = existing.HolidayScope == HolidayScope.Facility
            ? await _auth.RequireVerifiedFacilityManagerAsync(existing.FacilityId, cancellationToken)
            : await _auth.RequireVerifiedStaffPermissionAsync(cancellationToken, Permissions.Global.ComplianceManage);

        if (existing.ConfigRevision != request.ExpectedConfigRevision)
        {
            throw new ConcurrencyException(
                "HOLIDAY_STALE_REVISION",
                $"Holiday '{existing.Name}' was modified concurrently (expected revision {request.ExpectedConfigRevision}, actual {existing.ConfigRevision}).");
        }

        existing.Update(request.Name, request.IsWorkingDayOverride, request.IsActive);
        existing.UpdatedBy = staffUserId;
        await _db.SaveChangesAsync(cancellationToken);
        return existing.ToDto();
    }
}

public sealed record DeactivateHolidayCalendarCommand(Guid HolidayId, long? ExpectedConfigRevision = null)
    : IRequest<HolidayCalendarDto>;

public sealed class DeactivateHolidayCalendarCommandHandler
    : IRequestHandler<DeactivateHolidayCalendarCommand, HolidayCalendarDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public DeactivateHolidayCalendarCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<HolidayCalendarDto> Handle(DeactivateHolidayCalendarCommand request, CancellationToken cancellationToken)
    {
        var existing = await _db.HolidayCalendars
            .FirstOrDefaultAsync(h => h.Id == request.HolidayId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(HolidayCalendar), request.HolidayId);

        Guid staffUserId = existing.HolidayScope == HolidayScope.Facility
            ? await _auth.RequireVerifiedFacilityManagerAsync(existing.FacilityId, cancellationToken)
            : await _auth.RequireVerifiedStaffPermissionAsync(cancellationToken, Permissions.Global.ComplianceManage);

        if (request.ExpectedConfigRevision.HasValue &&
            existing.ConfigRevision != request.ExpectedConfigRevision.Value)
        {
            throw new ConcurrencyException(
                "HOLIDAY_STALE_REVISION",
                $"Holiday '{existing.Name}' was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {existing.ConfigRevision}).");
        }

        existing.Deactivate();
        existing.UpdatedBy = staffUserId;
        await _db.SaveChangesAsync(cancellationToken);
        return existing.ToDto();
    }
}

public sealed record UpsertFacilityCalendarCommand(
    Guid FacilityId,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string WeeklyRestDaysCsv,
    TimeSpan WorkdayStartLocalTime,
    TimeSpan WorkdayEndLocalTime,
    TimeSpan DefaultCutoffLocalTime,
    bool SupersedeOverlapping = false,
    long? ExpectedSupersedeRevision = null) : IRequest<FacilityCalendarDto>;

public sealed class UpsertFacilityCalendarCommandValidator : AbstractValidator<UpsertFacilityCalendarCommand>
{
    public UpsertFacilityCalendarCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.WeeklyRestDaysCsv).NotNull().MaximumLength(80);
    }
}

public sealed class UpsertFacilityCalendarCommandHandler
    : IRequestHandler<UpsertFacilityCalendarCommand, FacilityCalendarDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public UpsertFacilityCalendarCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<FacilityCalendarDto> Handle(UpsertFacilityCalendarCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedFacilityManagerAsync(request.FacilityId, cancellationToken);

        var facilityExists = await _db.Facilities
            .AsNoTracking()
            .AnyAsync(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive, cancellationToken);
        if (!facilityExists)
        {
            throw new EntityNotFoundException("Facility", request.FacilityId);
        }

        return await _db.ExecuteWithFacilityCalendarLockAsync(
            request.FacilityId,
            async () =>
            {
                var candidate = FacilityCalendar.Create(
                    request.FacilityId,
                    request.EffectiveFromUtc,
                    request.EffectiveToUtc,
                    request.WeeklyRestDaysCsv,
                    request.WorkdayStartLocalTime,
                    request.WorkdayEndLocalTime,
                    request.DefaultCutoffLocalTime);
                candidate.CreatedBy = staffUserId;

                var activeRows = await _db.FacilityCalendars
                    .Where(fc => fc.FacilityId == request.FacilityId && fc.IsActive)
                    .ToListAsync(cancellationToken);

                var overlapping = activeRows
                    .Where(fc => fc.OverlapsWindow(candidate.EffectiveFromUtc, candidate.EffectiveToUtc))
                    .ToList();

                if (overlapping.Count > 0)
                {
                    if (request.ExpectedSupersedeRevision.HasValue &&
                        !overlapping.Any(o => o.ConfigRevision == request.ExpectedSupersedeRevision.Value))
                    {
                        throw new ConcurrencyException(
                            "FACILITY_CALENDAR_STALE_REVISION",
                            $"Expected overlapping FacilityCalendar revision {request.ExpectedSupersedeRevision.Value} is no longer active.");
                    }

                    if (!request.SupersedeOverlapping)
                    {
                        throw new ConcurrencyException(
                            "FACILITY_CALENDAR_OVERLAP",
                            $"An active FacilityCalendar already covers an overlapping window for facility '{request.FacilityId}'. Pass SupersedeOverlapping=true to replace it.");
                    }

                    // Deactivate overlapping active rows FIRST and flush so PostgreSQL's non-deferred EXCLUDE USING gist never sees 2 active overlapping rows.
                    foreach (var o in overlapping)
                    {
                        o.Deactivate();
                        o.UpdatedBy = staffUserId;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }

                _db.FacilityCalendars.Add(candidate);
                await _db.SaveChangesAsync(cancellationToken);
                return candidate.ToDto();
            },
            cancellationToken);
    }
}
