using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Country, regional subdivision, or facility-scoped public holiday / working-day override entry.
/// Evaluated in facility local calendar dates with deterministic precedence: Facility &gt; Regional &gt; National.
/// </summary>
public class HolidayCalendar : AuditableEntity<Guid>
{
    public string CountryCode { get; set; } = string.Empty;
    public string? SubdivisionCode { get; set; }
    public Guid? FacilityId { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = string.Empty;
    public HolidayScope HolidayScope { get; set; }
    public bool IsWorkingDayOverride { get; set; }
    public long ConfigRevision { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public CountryProfile? Country { get; set; }
    public Facility? Facility { get; set; }

    public HolidayCalendar()
    {
        Id = Guid.NewGuid();
    }

    public static HolidayCalendar Create(
        string countryCode,
        HolidayScope scope,
        DateOnly holidayDate,
        string name,
        string? subdivisionCode = null,
        Guid? facilityId = null,
        bool isWorkingDayOverride = false,
        Guid? id = null)
    {
        var normalizedCountry = ValidateScopeAndNormalize(countryCode, scope, subdivisionCode, facilityId, name);

        var entry = new HolidayCalendar
        {
            CountryCode = normalizedCountry,
            SubdivisionCode = string.IsNullOrWhiteSpace(subdivisionCode) ? null : subdivisionCode.Trim().ToUpperInvariant(),
            FacilityId = facilityId.HasValue && facilityId.Value != Guid.Empty ? facilityId.Value : null,
            HolidayDate = holidayDate,
            Name = name.Trim(),
            HolidayScope = scope,
            IsWorkingDayOverride = isWorkingDayOverride,
            ConfigRevision = 1,
            IsActive = true
        };

        if (id.HasValue && id.Value != Guid.Empty)
        {
            entry.Id = id.Value;
        }

        return entry;
    }

    public void Update(string name, bool isWorkingDayOverride, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new DomainException("Holiday Name is required and must not exceed 120 characters.", "HOLIDAY_INVALID_NAME");
        }

        Name = name.Trim();
        IsWorkingDayOverride = isWorkingDayOverride;
        IsActive = isActive;
        ConfigRevision++;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        ConfigRevision++;
    }

    private static string ValidateScopeAndNormalize(
        string countryCode,
        HolidayScope scope,
        string? subdivisionCode,
        Guid? facilityId,
        string name)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            throw new DomainException("Holiday CountryCode must be a 2-letter ISO 3166-1 alpha-2 code.", "HOLIDAY_INVALID_COUNTRY");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new DomainException("Holiday Name is required and must not exceed 120 characters.", "HOLIDAY_INVALID_NAME");
        }

        switch (scope)
        {
            case HolidayScope.National:
                if (!string.IsNullOrWhiteSpace(subdivisionCode) || (facilityId.HasValue && facilityId.Value != Guid.Empty))
                {
                    throw new DomainException(
                        "National holiday entries cannot specify SubdivisionCode or FacilityId.",
                        "HOLIDAY_NATIONAL_SCOPE_CONFLICT");
                }
                break;

            case HolidayScope.Regional:
                if (string.IsNullOrWhiteSpace(subdivisionCode))
                {
                    throw new DomainException(
                        "Regional holiday entries require a non-empty SubdivisionCode.",
                        "HOLIDAY_REGIONAL_SUBDIVISION_REQUIRED");
                }
                if (facilityId.HasValue && facilityId.Value != Guid.Empty)
                {
                    throw new DomainException(
                        "Regional holiday entries cannot specify FacilityId.",
                        "HOLIDAY_REGIONAL_FACILITY_CONFLICT");
                }
                break;

            case HolidayScope.Facility:
                if (!facilityId.HasValue || facilityId.Value == Guid.Empty)
                {
                    throw new DomainException(
                        "Facility holiday entries require a valid FacilityId.",
                        "HOLIDAY_FACILITY_REQUIRED");
                }
                break;

            default:
                throw new DomainException($"Unsupported HolidayScope '{scope}'.", "HOLIDAY_INVALID_SCOPE");
        }

        return countryCode.Trim().ToUpperInvariant();
    }
}
