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

public sealed record CreateFacilityCommand(
    string FacilityCode,
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
    int DockDoorCount = 4) : IRequest<FacilityDto>;

public sealed class CreateFacilityCommandValidator : AbstractValidator<CreateFacilityCommand>
{
    public CreateFacilityCommandValidator()
    {
        RuleFor(x => x.FacilityCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryCode).NotEmpty().Length(2).Matches("^[A-Za-z]{2}$");
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DefaultCurrencyCode).Length(3).When(x => !string.IsNullOrWhiteSpace(x.DefaultCurrencyCode));
        RuleFor(x => x.DockDoorCount).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateFacilityCommandHandler : IRequestHandler<CreateFacilityCommand, FacilityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;
    private readonly ITimeZoneOffsetResolver _timeZoneResolver;

    public CreateFacilityCommandHandler(
        IApplicationDbContext db,
        CompliancePlatformAuthorization auth,
        ITimeZoneOffsetResolver timeZoneResolver)
    {
        _db = db;
        _auth = auth;
        _timeZoneResolver = timeZoneResolver;
    }

    public async Task<FacilityDto> Handle(CreateFacilityCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedFacilityManagerAsync(null, cancellationToken);

        var normalizedFacilityCode = request.FacilityCode.Trim().ToUpperInvariant();
        Guid[] lockIds = request.DefaultLegalEntityId.HasValue && request.DefaultLegalEntityId.Value != Guid.Empty
            ? [request.DefaultLegalEntityId.Value]
            : [];

        return await _db.ExecuteWithLegalEntityLocksAsync(lockIds, async () =>
        {
            bool duplicateCode = await _db.Facilities
                .AsNoTracking()
                .AnyAsync(f => !f.IsDeleted && f.FacilityCode == normalizedFacilityCode, cancellationToken);

            if (duplicateCode)
            {
                throw new ConcurrencyException(
                    "FACILITY_DUPLICATE_CODE",
                    $"Facility '{normalizedFacilityCode}' already exists.");
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

            var facility = new Facility
            {
                FacilityCode = normalizedFacilityCode,
                Name = request.Name.Trim(),
                AddressLine1 = string.IsNullOrWhiteSpace(request.AddressLine1) ? null : request.AddressLine1.Trim(),
                AddressLine2 = string.IsNullOrWhiteSpace(request.AddressLine2) ? null : request.AddressLine2.Trim(),
                City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
                StateOrProvince = string.IsNullOrWhiteSpace(request.StateOrProvince) ? null : request.StateOrProvince.Trim(),
                PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim(),
                CountryCode = normalizedCountry,
                Timezone = normalizedTz,
                DefaultLegalEntityId = request.DefaultLegalEntityId,
                DefaultCurrencyCode = normalizedCurrency,
                ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim(),
                ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
                TotalAreaSqM = request.TotalAreaSqM,
                DockDoorCount = request.DockDoorCount,
                IsActive = true,
                CreatedBy = staffUserId
            };

            _db.Facilities.Add(facility);
            await _db.SaveChangesAsync(cancellationToken);

            return FacilityProjections.ToDto(facility);
        }, cancellationToken);
    }
}
