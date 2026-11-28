using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Suppliers;

public record GetSupplierByIdQuery(Guid Id, Guid? ClientId = null) : IRequest<SupplierDto>;

public record UpdateSupplierCommand : IRequest<SupplierDto>
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public string? SupplierCode { get; init; }
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
    public int? ExpectedConfigRevision { get; init; }
}

public record SetSupplierStatusCommand(Guid Id, bool IsActive, Guid? ClientId = null, int? ExpectedConfigRevision = null)
    : IRequest<SupplierDto>;

public record AddSupplierContactCommand : IRequest<SupplierContactDto>
{
    public Guid SupplierId { get; init; }
    public Guid? ClientId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsPrimary { get; init; }
}

public record UpdateSupplierContactCommand : IRequest<SupplierContactDto>
{
    public Guid SupplierId { get; init; }
    public Guid ContactId { get; init; }
    public Guid? ClientId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public int? ExpectedConfigRevision { get; init; }
}

public record DeleteSupplierContactCommand(Guid SupplierId, Guid ContactId, Guid? ClientId = null) : IRequest<Unit>;

public class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.SupplierName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
        RuleFor(x => x.PaymentTermsDays).GreaterThanOrEqualTo(0).When(x => x.PaymentTermsDays.HasValue);
        RuleFor(x => x.LeadTimeDays).GreaterThanOrEqualTo(0).When(x => x.LeadTimeDays.HasValue);
    }
}

public class AddSupplierContactCommandValidator : AbstractValidator<AddSupplierContactCommand>
{
    public AddSupplierContactCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
    }
}

internal static class SupplierLifecycleAndContactHandlersMapping
{
    public static SupplierDto ToDto(Supplier s) => new()
    {
        Id = s.Id,
        ClientId = s.ClientId,
        SupplierCode = s.SupplierCode,
        SupplierName = s.SupplierName,
        TaxRegistrationNumber = s.TaxRegistrationNumber,
        PaymentTermsDays = s.PaymentTermsDays,
        LeadTimeDays = s.LeadTimeDays,
        ContactName = s.ContactName,
        Email = s.Email,
        Phone = s.Phone,
        AddressLine1 = s.AddressLine1,
        AddressLine2 = s.AddressLine2,
        City = s.City,
        State = s.State,
        PostalCode = s.PostalCode,
        Country = s.Country,
        CountryNormalizationStatus = s.CountryNormalizationStatus,
        IsActive = s.IsActive,
        ConfigRevision = s.ConfigRevision,
        CreatedAt = s.CreatedAt,
        Contacts = s.Contacts
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.LastName)
            .Select(ToContactDto)
            .ToList()
    };

    public static SupplierContactDto ToContactDto(SupplierContact c) => new()
    {
        Id = c.Id,
        ClientId = c.ClientId,
        SupplierId = c.SupplierId,
        FirstName = c.FirstName,
        LastName = c.LastName,
        RoleTitle = c.RoleTitle,
        Email = c.Email,
        Phone = c.Phone,
        IsPrimary = c.IsPrimary,
        IsActive = c.IsActive,
        ConfigRevision = c.ConfigRevision
    };
}

public class GetSupplierByIdQueryHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetSupplierByIdQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<SupplierDto> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersView, Permissions.Clients.View]);

        var supplier = await _context.Suppliers
            .IgnoreQueryFilters()
            .Include(s => s.Contacts)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.ClientId == clientId && !s.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Supplier), request.Id);

        return SupplierLifecycleAndContactHandlersMapping.ToDto(supplier);
    }
}

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, SupplierDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateSupplierCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<SupplierDto> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersEdit, Permissions.Clients.Edit]);

        var supplier = await _context.Suppliers
            .IgnoreQueryFilters()
            .Include(s => s.Contacts)
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.ClientId == clientId && !s.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Supplier), request.Id);

        if (!string.IsNullOrWhiteSpace(request.SupplierCode) &&
            !string.Equals(Supplier.NormalizeCode(request.SupplierCode), supplier.NormalizedSupplierCode, StringComparison.Ordinal))
        {
            throw new DomainException($"Supplier code '{supplier.SupplierCode}' is immutable after creation.", "SUPPLIER_CODE_IMMUTABLE");
        }

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != supplier.ConfigRevision)
        {
            throw new ConcurrencyException("SUPPLIER_CONCURRENCY_CONFLICT", $"Supplier '{supplier.SupplierCode}' was modified concurrently.");
        }

        var (resolvedCountry, resolvedStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.Country, request.State, request.PostalCode, "Address", cancellationToken);

        supplier.SupplierName = request.SupplierName.Trim();
        supplier.TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim();
        supplier.PaymentTermsDays = request.PaymentTermsDays;
        supplier.LeadTimeDays = request.LeadTimeDays;
        supplier.ContactName = request.ContactName?.Trim();
        supplier.Email = request.Email?.Trim().ToLowerInvariant();
        supplier.Phone = request.Phone?.Trim();
        supplier.AddressLine1 = request.AddressLine1?.Trim();
        supplier.AddressLine2 = request.AddressLine2?.Trim();
        supplier.City = request.City?.Trim();
        supplier.State = request.State?.Trim();
        supplier.PostalCode = request.PostalCode?.Trim();
        supplier.Country = resolvedCountry;
        supplier.CountryNormalizationStatus = resolvedStatus;
        supplier.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return SupplierLifecycleAndContactHandlersMapping.ToDto(supplier);
    }
}

public class SetSupplierStatusCommandHandler : IRequestHandler<SetSupplierStatusCommand, SupplierDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public SetSupplierStatusCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<SupplierDto> Handle(SetSupplierStatusCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersEdit, Permissions.Inbound.SuppliersDelete, Permissions.Clients.Edit]);

        var supplier = await _context.Suppliers
            .IgnoreQueryFilters()
            .Include(s => s.Contacts)
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.ClientId == clientId && !s.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Supplier), request.Id);

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != supplier.ConfigRevision)
        {
            throw new ConcurrencyException("SUPPLIER_CONCURRENCY_CONFLICT", $"Supplier '{supplier.SupplierCode}' was modified concurrently.");
        }

        // Deactivating a supplier preserves historical PurchaseOrders & ASNs while blocking new inbound creation.
        supplier.IsActive = request.IsActive;
        supplier.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return SupplierLifecycleAndContactHandlersMapping.ToDto(supplier);
    }
}

public class AddSupplierContactCommandHandler : IRequestHandler<AddSupplierContactCommand, SupplierContactDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public AddSupplierContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<SupplierContactDto> Handle(AddSupplierContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersEdit, Permissions.Clients.Edit]);

        var supplierExists = await _context.Suppliers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SupplierId && s.ClientId == clientId && !s.IsDeleted, cancellationToken);
        if (!supplierExists)
        {
            throw new EntityNotFoundException(nameof(Supplier), request.SupplierId);
        }

        var contact = new SupplierContact
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            SupplierId = request.SupplierId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            RoleTitle = request.RoleTitle?.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            IsPrimary = request.IsPrimary,
            IsActive = true,
            ConfigRevision = 1
        };

        _context.SupplierContacts.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return SupplierLifecycleAndContactHandlersMapping.ToContactDto(contact);
    }
}

public class UpdateSupplierContactCommandHandler : IRequestHandler<UpdateSupplierContactCommand, SupplierContactDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateSupplierContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<SupplierContactDto> Handle(UpdateSupplierContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersEdit, Permissions.Clients.Edit]);

        var contact = await _context.SupplierContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.SupplierId == request.SupplierId && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupplierContact), request.ContactId);

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != contact.ConfigRevision)
        {
            throw new ConcurrencyException("SUPPLIER_CONTACT_CONCURRENCY_CONFLICT", "Supplier contact was modified concurrently.");
        }

        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.RoleTitle = request.RoleTitle?.Trim();
        contact.Email = request.Email.Trim().ToLowerInvariant();
        contact.Phone = request.Phone?.Trim();
        contact.IsPrimary = request.IsPrimary;
        contact.IsActive = request.IsActive;
        contact.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return SupplierLifecycleAndContactHandlersMapping.ToContactDto(contact);
    }
}

public class DeleteSupplierContactCommandHandler : IRequestHandler<DeleteSupplierContactCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public DeleteSupplierContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<Unit> Handle(DeleteSupplierContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Inbound.SuppliersEdit, Permissions.Inbound.SuppliersDelete, Permissions.Clients.Edit]);

        var contact = await _context.SupplierContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.SupplierId == request.SupplierId && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupplierContact), request.ContactId);

        contact.IsActive = false;
        contact.IsPrimary = false;
        contact.IsDeleted = true;
        contact.DeletedAt = DateTime.UtcNow;
        contact.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
