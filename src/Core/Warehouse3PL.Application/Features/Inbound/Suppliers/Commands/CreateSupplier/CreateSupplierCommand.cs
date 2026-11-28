using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;

public record CreateSupplierCommand : IRequest<SupplierDto>
{
    public Guid? ClientId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string? TaxRegistrationNumber { get; init; }
    public int? PaymentTermsDays { get; init; }
    public int? LeadTimeDays { get; init; }
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public List<CreateSupplierContactInput> Contacts { get; init; } = [];
}

public record CreateSupplierContactInput
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsPrimary { get; init; }
}

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.SupplierCode)
            .NotEmpty().WithMessage("Supplier code is required.")
            .MaximumLength(50).WithMessage("Supplier code must not exceed 50 characters.")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Supplier code must only contain alphanumeric characters, hyphens, or underscores.");

        RuleFor(x => x.SupplierName)
            .NotEmpty().WithMessage("Supplier name is required.")
            .MaximumLength(200).WithMessage("Supplier name must not exceed 200 characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Invalid email format.");

        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
        RuleFor(x => x.PaymentTermsDays).GreaterThanOrEqualTo(0).When(x => x.PaymentTermsDays.HasValue);
        RuleFor(x => x.LeadTimeDays).GreaterThanOrEqualTo(0).When(x => x.LeadTimeDays.HasValue);

        RuleForEach(x => x.Contacts).ChildRules(c =>
        {
            c.RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
            c.RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
            c.RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        });
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, SupplierDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public CreateSupplierCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        PartnerMasterAuthorization? auth = null)
    {
        _context = context;
        _auth = auth ?? new PartnerMasterAuthorization(context, currentUser);
    }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var (_, targetClientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersCreate, Permissions.Clients.Edit]);

        var client = await _context.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == targetClientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("Client", targetClientId);

        if (client.Status != ClientStatus.Active)
        {
            throw new DomainException($"Client '{client.AccountCode}' is '{client.Status}' and cannot create new suppliers.", "CLIENT_INACTIVE");
        }

        // MDM-2.2: Fix case-sensitivity bug permanently using NormalizedSupplierCode across ALL rows (including archived)
        var normalizedCode = Supplier.NormalizeCode(request.SupplierCode);
        var exists = await _context.Suppliers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s => s.ClientId == targetClientId &&
                           (s.NormalizedSupplierCode == normalizedCode || s.SupplierCode.ToUpper() == normalizedCode),
                cancellationToken);

        if (exists)
        {
            throw new DomainException(
                $"Supplier with code '{normalizedCode}' already exists for this client (including archived records).",
                "SUPPLIER_CODE_DUPLICATE");
        }

        var (resolvedCountry, resolvedStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context,
            request.Country,
            request.State,
            request.PostalCode,
            "Address",
            cancellationToken);

        var supplier = new Supplier
        {
            ClientId = targetClientId,
            SupplierCode = normalizedCode,
            NormalizedSupplierCode = normalizedCode,
            SupplierName = request.SupplierName.Trim(),
            TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim(),
            PaymentTermsDays = request.PaymentTermsDays,
            LeadTimeDays = request.LeadTimeDays,
            ContactName = request.ContactName?.Trim(),
            Email = request.Email?.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            AddressLine1 = request.AddressLine1?.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            Country = resolvedCountry,
            CountryNormalizationStatus = resolvedStatus,
            IsActive = true,
            ConfigRevision = 1
        };

        foreach (var c in request.Contacts)
        {
            supplier.Contacts.Add(new SupplierContact
            {
                Id = Guid.NewGuid(),
                ClientId = targetClientId,
                SupplierId = supplier.Id,
                FirstName = c.FirstName.Trim(),
                LastName = c.LastName.Trim(),
                RoleTitle = c.RoleTitle?.Trim(),
                Email = c.Email.Trim().ToLowerInvariant(),
                Phone = c.Phone?.Trim(),
                IsPrimary = c.IsPrimary,
                IsActive = true,
                ConfigRevision = 1
            });
        }

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);

        return SupplierLifecycleAndContactHandlersMapping.ToDto(supplier);
    }
}
