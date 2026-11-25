using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Facilities.Queries;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Facilities.Commands;

public sealed record UpdateFacilityCommand(
    Guid Id,
    string? FacilityCode,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateOrProvince,
    string? PostalCode,
    string CountryCode,
    string Timezone,
    Guid? DefaultLegalEntityId,
    string? DefaultCurrencyCode,
    string? ContactEmail,
    string? ContactPhone,
    decimal? TotalAreaSqM,
    int DockDoorCount,
    bool IsActive) : IRequest<FacilityDto>;

public sealed class UpdateFacilityCommandValidator : AbstractValidator<UpdateFacilityCommand>
{
    public UpdateFacilityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryCode).NotEmpty().Length(2).Matches("^[A-Za-z]{2}$");
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DefaultCurrencyCode).Length(3).When(x => !string.IsNullOrWhiteSpace(x.DefaultCurrencyCode));
        RuleFor(x => x.DockDoorCount).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateFacilityCommandHandler : IRequestHandler<UpdateFacilityCommand, FacilityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly ITimeZoneOffsetResolver _timeZoneResolver;

    public UpdateFacilityCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        ITimeZoneOffsetResolver timeZoneResolver)
    {
        _db = db;
        _auth = auth;
        _timeZoneResolver = timeZoneResolver;
    }

    public async Task<FacilityDto> Handle(UpdateFacilityCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedFacilityManagerAsync(request.Id, cancellationToken);

        var facility = await _db.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.Id && !f.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Facility), request.Id);

        if (!string.IsNullOrWhiteSpace(request.FacilityCode) &&
            !string.Equals(request.FacilityCode.Trim(), facility.FacilityCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "FACILITY_IMMUTABLE_CODE",
                $"FacilityCode '{facility.FacilityCode}' is immutable and cannot be changed to '{request.FacilityCode}'.");
        }

        var (normalizedCountry, normalizedTz, normalizedCurrency) =
            await FacilityReferenceValidationHelper.ValidateGeographicAndLegalReferencesAsync(
                _db,
                _timeZoneResolver,
                request.CountryCode,
                request.Timezone,
                request.PostalCode,
                request.DefaultCurrencyCode,
                request.DefaultLegalEntityId,
                cancellationToken);

        return await _db.ExecuteInTransactionAsync(async () =>
        {
            facility.Name = request.Name.Trim();
            facility.AddressLine1 = string.IsNullOrWhiteSpace(request.AddressLine1) ? null : request.AddressLine1.Trim();
            facility.AddressLine2 = string.IsNullOrWhiteSpace(request.AddressLine2) ? null : request.AddressLine2.Trim();
            facility.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
            facility.StateOrProvince = string.IsNullOrWhiteSpace(request.StateOrProvince) ? null : request.StateOrProvince.Trim();
            facility.PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim();
            facility.CountryCode = normalizedCountry;
            facility.Timezone = normalizedTz;
            facility.DefaultLegalEntityId = request.DefaultLegalEntityId;
            facility.DefaultCurrencyCode = normalizedCurrency;
            facility.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim();
            facility.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
            facility.TotalAreaSqM = request.TotalAreaSqM;
            facility.DockDoorCount = request.DockDoorCount;
            facility.IsActive = request.IsActive;
            facility.UpdatedBy = staffUserId;

            await _db.SaveChangesAsync(cancellationToken);
            return FacilityProjections.ToDto(facility);
        }, cancellationToken);
    }
}
