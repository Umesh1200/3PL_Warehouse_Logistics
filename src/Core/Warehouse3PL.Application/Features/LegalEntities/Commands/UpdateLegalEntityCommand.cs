using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.LegalEntities.Commands;

public sealed record UpdateLegalEntityCommand(
    Guid Id,
    string? LegalCode,
    string LegalName,
    string? RegistrationNumber,
    string CountryCode,
    TaxIdentifierType TaxIdentifierType,
    string TaxRegistrationNumber,
    string RegisteredAddressLine1,
    string? RegisteredAddressLine2,
    string RegisteredCity,
    string? RegisteredStateOrProvince,
    string? RegisteredPostalCode,
    bool IsCbmsMandated,
    string? IroSoftwarePermitRef,
    string DefaultCurrencyCode,
    long? ExpectedConfigRevision = null) : IRequest<LegalEntityDto>;

public sealed class UpdateLegalEntityCommandValidator : AbstractValidator<UpdateLegalEntityCommand>
{
    public UpdateLegalEntityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryCode).NotEmpty().Length(2).Matches("^[A-Za-z]{2}$");
        RuleFor(x => x.TaxRegistrationNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RegisteredAddressLine1).NotEmpty().MaximumLength(255);
        RuleFor(x => x.RegisteredCity).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RegisteredPostalCode).MaximumLength(30);
        RuleFor(x => x.IroSoftwarePermitRef).MaximumLength(120);
        RuleFor(x => x.DefaultCurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
    }
}

public sealed class UpdateLegalEntityCommandHandler : IRequestHandler<UpdateLegalEntityCommand, LegalEntityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public UpdateLegalEntityCommandHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LegalEntityDto> Handle(UpdateLegalEntityCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageLegalEntities);

        var entity = await _db.LegalEntities
            .FirstOrDefaultAsync(le => le.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(LegalEntity), request.Id);

        if (request.ExpectedConfigRevision.HasValue &&
            entity.ConfigRevision != request.ExpectedConfigRevision.Value)
        {
            throw new ConcurrencyException(
                "LEGAL_ENTITY_STALE_REVISION",
                $"LegalEntity '{entity.LegalCode}' was modified concurrently (expected revision {request.ExpectedConfigRevision.Value}, actual {entity.ConfigRevision}).");
        }

        if (!string.IsNullOrWhiteSpace(request.LegalCode) &&
            !string.Equals(request.LegalCode.Trim(), entity.LegalCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "LEGAL_ENTITY_IMMUTABLE_CODE",
                $"LegalCode '{entity.LegalCode}' is immutable and cannot be changed to '{request.LegalCode}'.");
        }

        var normalizedCountry = request.CountryCode.Trim().ToUpperInvariant();
        var country = await _db.CountryProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryCode == normalizedCountry && c.IsActive, cancellationToken)
            ?? throw new DomainException(
                "LEGAL_ENTITY_UNKNOWN_COUNTRY",
                $"CountryCode '{normalizedCountry}' is not an active registered CountryProfile.");

        if (!string.IsNullOrWhiteSpace(request.RegisteredPostalCode) &&
            !country.IsSyntacticallyValidPostalCode(request.RegisteredPostalCode, out _))
        {
            throw new DomainException(
                "LEGAL_ENTITY_INVALID_POSTAL_CODE",
                $"RegisteredPostalCode '{request.RegisteredPostalCode}' does not match syntactic postal format for '{normalizedCountry}'.");
        }

        if (entity.IsCbmsMandated && !request.IsCbmsMandated)
        {
            var boundFacilityIds = await _db.Facilities
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.DefaultLegalEntityId == entity.Id)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);

            if (boundFacilityIds.Count > 0)
            {
                bool hasIssuedInvoices = await _db.Invoices
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .AnyAsync(i => i.FacilityId != null &&
                                   boundFacilityIds.Contains(i.FacilityId.Value) &&
                                   i.Status != InvoiceStatus.Draft &&
                                   i.Status != InvoiceStatus.Voided,
                        cancellationToken);

                if (hasIssuedInvoices)
                {
                    throw new ConcurrencyException(
                        "LEGAL_ENTITY_CBMS_MANDATE_LOCKED",
                        $"Cannot disable IsCbmsMandated on LegalEntity '{entity.LegalCode}' because bound facilities have issued historical invoices.");
                }
            }
        }

        entity.UpdateDetails(
            request.LegalName,
            request.RegistrationNumber,
            normalizedCountry,
            request.TaxIdentifierType,
            request.TaxRegistrationNumber,
            request.RegisteredAddressLine1,
            request.RegisteredAddressLine2,
            request.RegisteredCity,
            request.RegisteredStateOrProvince,
            request.RegisteredPostalCode,
            request.IsCbmsMandated,
            request.IroSoftwarePermitRef,
            request.DefaultCurrencyCode);

        entity.UpdatedBy = staffUserId;
        await _db.SaveChangesAsync(cancellationToken);

        return entity.ToDto();
    }
}
