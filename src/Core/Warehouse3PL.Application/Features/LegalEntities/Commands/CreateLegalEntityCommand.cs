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

public sealed record CreateLegalEntityCommand(
    string LegalCode,
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
    string DefaultCurrencyCode) : IRequest<LegalEntityDto>;

public sealed class CreateLegalEntityCommandValidator : AbstractValidator<CreateLegalEntityCommand>
{
    public CreateLegalEntityCommandValidator()
    {
        RuleFor(x => x.LegalCode).NotEmpty().MaximumLength(40);
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

public sealed class CreateLegalEntityCommandHandler : IRequestHandler<CreateLegalEntityCommand, LegalEntityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly CompliancePlatformAuthorization _auth;

    public CreateLegalEntityCommandHandler(IApplicationDbContext db, CompliancePlatformAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LegalEntityDto> Handle(CreateLegalEntityCommand request, CancellationToken cancellationToken)
    {
        var staffUserId = await _auth.RequireVerifiedStaffPermissionAsync(
            cancellationToken,
            Permissions.Global.ManageLegalEntities);

        var normalizedCode = request.LegalCode.Trim().ToUpperInvariant();
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

        bool duplicateCode = await _db.LegalEntities
            .AsNoTracking()
            .AnyAsync(le => le.LegalCode == normalizedCode, cancellationToken);

        if (duplicateCode)
        {
            throw new ConcurrencyException(
                "LEGAL_ENTITY_DUPLICATE_CODE",
                $"A LegalEntity with LegalCode '{normalizedCode}' already exists.");
        }

        var entity = LegalEntity.Create(
            normalizedCode,
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

        entity.CreatedBy = staffUserId;
        _db.LegalEntities.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return entity.ToDto();
    }
}
