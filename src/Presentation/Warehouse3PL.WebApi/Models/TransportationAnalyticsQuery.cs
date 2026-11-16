using System.ComponentModel.DataAnnotations;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Models;

public class TransportationScopeQuery : IValidatableObject
{
    public Guid? ClientId { get; set; }
    public Guid? FacilityId { get; set; }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClientId == Guid.Empty) yield return new("ClientId must be a non-empty UUID.", [nameof(ClientId)]);
        if (FacilityId == Guid.Empty) yield return new("FacilityId must be a non-empty UUID.", [nameof(FacilityId)]);
    }
}

public class TransportationAnalyticsQuery : TransportationScopeQuery
{
    [Required] public DateTime? StartDateUtc { get; set; }
    [Required] public DateTime? EndDateUtc { get; set; }
    // Calendar dates without an offset are interpreted as UTC, never as host-local dates.
    internal DateTime Start => Utc(StartDateUtc!.Value).Date;
    internal DateTime End => Utc(EndDateUtc!.Value).Date;
    private static DateTime Utc(DateTime date) => date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);

    public override IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var error in base.Validate(context)) yield return error;
        if (StartDateUtc.HasValue && EndDateUtc.HasValue &&
            (End < Start || (End - Start).TotalDays > 365 || End.Year == 9999))
            yield return new("Use an ordered UTC date range of at most 366 inclusive days, before year 9999.", [nameof(StartDateUtc), nameof(EndDateUtc)]);
    }
}

public class CarrierPerformanceQuery : TransportationAnalyticsQuery
{
    public CarrierServiceLevel? ServiceLevel { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var error in base.Validate(context)) yield return error;
        if (ServiceLevel.HasValue && !Enum.IsDefined(ServiceLevel.Value)) yield return new("Unknown service level.", [nameof(ServiceLevel)]);
    }
}

public class TransitDistributionQuery : CarrierPerformanceQuery
{
    [StringLength(3, MinimumLength = 2)] public string? DestinationCountry { get; set; }
    [StringLength(100)] public string? DestinationState { get; set; }
}

public class FreightAnalyticsQuery : TransportationAnalyticsQuery
{
    public DateTimeOffset? AsOfUtc { get; set; }
}

public class FreightProfitabilityQuery : FreightAnalyticsQuery
{
    public CarrierServiceLevel? ServiceLevel { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var error in base.Validate(context)) yield return error;
        if (ServiceLevel.HasValue && !Enum.IsDefined(ServiceLevel.Value)) yield return new("Unknown service level.", [nameof(ServiceLevel)]);
    }
}

public class FreightDiscrepancyQuery : FreightAnalyticsQuery
{
    public FreightReconciliationStatus? StatusFilter { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? MinimumDiscrepancyUsd { get; set; }
    [Range(1, 1000000)] public int PageNumber { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
    public override IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var error in base.Validate(context)) yield return error;
        if (StatusFilter.HasValue && !Enum.IsDefined(StatusFilter.Value)) yield return new("Unknown reconciliation status.", [nameof(StatusFilter)]);
    }
}

public class ClaimsReadinessQuery : IValidatableObject
{
    public ClaimType ClaimType { get; set; } = ClaimType.Lost;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!Enum.IsDefined(ClaimType)) yield return new("Unknown claim type.", [nameof(ClaimType)]);
    }
}

public class ClaimsReadinessPageQuery : TransportationScopeQuery
{
    [Range(1, 1000000)] public int PageNumber { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}
