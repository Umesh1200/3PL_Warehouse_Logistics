using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Clients.Commands.CreateClient;

public record CreateClientCommand : IRequest<ClientDto>
{
    public string CompanyName { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public ClientStatus Status { get; init; } = ClientStatus.Active;
    public SlaTier SlaTier { get; init; } = SlaTier.Standard;
    public string Currency { get; init; } = "USD";
    public string? BusinessClassification { get; init; }
    public string? TaxRegistrationNumber { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
    public List<CreateClientContactDto> Contacts { get; init; } = [];
}

public record CreateClientContactDto
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsBillingContact { get; init; }
    public bool IsPrimary { get; init; }
}

public class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name must not exceed 200 characters.");

        RuleFor(x => x.AccountCode)
            .NotEmpty().WithMessage("Account code is required.")
            .MaximumLength(50).WithMessage("Account code must not exceed 50 characters.")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Account code must only contain alphanumeric characters, hyphens, or underscores.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code (e.g., USD, EUR).");

        RuleFor(x => x.BusinessClassification).MaximumLength(100);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
        RuleFor(x => x.BillingAddressLine1).MaximumLength(255);
        RuleFor(x => x.BillingAddressLine2).MaximumLength(255);
        RuleFor(x => x.BillingCity).MaximumLength(100);
        RuleFor(x => x.BillingStateOrProvince).MaximumLength(100);
        RuleFor(x => x.BillingPostalCode).MaximumLength(30);
        RuleFor(x => x.BillingCountryCode).MaximumLength(50);

        RuleForEach(x => x.Contacts).ChildRules(contact =>
        {
            contact.RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
            contact.RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);
            contact.RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(255);
            contact.RuleFor(c => c.Phone).MaximumLength(50);
            contact.RuleFor(c => c.RoleTitle).MaximumLength(100);
        });
    }
}

public class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization? _auth;

    public CreateClientCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientDto> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        if (_auth != null)
        {
            await _auth.RequireStaffPermissionAsync(cancellationToken, Permissions.Clients.Create);
        }

        var normalizedAccountCode = Client.NormalizeCode(request.AccountCode);

        // Permanent uniqueness check across ALL rows including soft-deleted records
        var exists = await _context.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.NormalizedAccountCode == normalizedAccountCode ||
                           c.AccountCode.ToUpper() == normalizedAccountCode,
                cancellationToken);

        if (exists)
        {
            throw new ValidationException(
                "AccountCode",
                $"A client with account code '{normalizedAccountCode}' already exists (including historical/archived records).");
        }

        var currency = _auth != null
            ? await _auth.ValidateRequiredCurrencyAsync(request.Currency, cancellationToken)
            : request.Currency.Trim().ToUpperInvariant();

        var (billingCountry, countryNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context,
            request.BillingCountryCode,
            request.BillingStateOrProvince,
            request.BillingPostalCode,
            "Billing",
            cancellationToken);

        var client = new Client
        {
            CompanyName = request.CompanyName.Trim(),
            AccountCode = normalizedAccountCode,
            NormalizedAccountCode = normalizedAccountCode,
            Status = request.Status,
            SlaTier = request.SlaTier,
            Currency = currency,
            BusinessClassification = request.BusinessClassification?.Trim(),
            TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim(),
            BillingAddressLine1 = request.BillingAddressLine1?.Trim(),
            BillingAddressLine2 = request.BillingAddressLine2?.Trim(),
            BillingCity = request.BillingCity?.Trim(),
            BillingStateOrProvince = request.BillingStateOrProvince?.Trim(),
            BillingPostalCode = request.BillingPostalCode?.Trim(),
            BillingCountryCode = billingCountry,
            CountryNormalizationStatus = countryNormStatus,
            ConfigRevision = 1
        };

        foreach (var contactDto in request.Contacts)
        {
            client.Contacts.Add(new ClientContact
            {
                ClientId = client.Id,
                FirstName = contactDto.FirstName.Trim(),
                LastName = contactDto.LastName.Trim(),
                RoleTitle = contactDto.RoleTitle?.Trim(),
                Email = contactDto.Email.Trim().ToLowerInvariant(),
                Phone = contactDto.Phone?.Trim(),
                IsBillingContact = contactDto.IsBillingContact,
                IsPrimary = contactDto.IsPrimary,
                IsActive = true,
                ConfigRevision = 1
            });
        }

        _context.Clients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);

        return client.Adapt<ClientDto>();
    }
}
