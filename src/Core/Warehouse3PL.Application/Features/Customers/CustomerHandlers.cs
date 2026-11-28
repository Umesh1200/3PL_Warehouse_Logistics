using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Helpers;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Customers;

public record GetCustomersQuery : PagedQuery, IRequest<PagedResult<CustomerDto>>
{
    public Guid? ClientId { get; init; }
    public bool? IsActive { get; init; }
    public CustomerType? CustomerType { get; init; }
}

public record GetCustomerByIdQuery(Guid Id, Guid? ClientId = null) : IRequest<CustomerDto>;

public record CreateCustomerCommand : IRequest<CustomerDto>
{
    public Guid? ClientId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public CustomerType CustomerType { get; init; } = CustomerType.RetailCustomer;
    public string? TaxRegistrationNumber { get; init; }
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    public string? DefaultAddressLine1 { get; init; }
    public string? DefaultAddressLine2 { get; init; }
    public string? DefaultCity { get; init; }
    public string? DefaultStateOrProvince { get; init; }
    public string? DefaultPostalCode { get; init; }
    public string? DefaultCountryCode { get; init; }

    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }

    public List<CreateCustomerAddressInput> Addresses { get; init; } = [];
}

public record CreateCustomerAddressInput
{
    public CustomerAddressType AddressType { get; init; } = CustomerAddressType.Delivery;
    public string? Label { get; init; }
    public string? RecipientName { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
    public string? Phone { get; init; }
    public string? DeliveryInstructions { get; init; }
    public bool IsDefault { get; init; }
}

public record UpdateCustomerCommand : IRequest<CustomerDto>
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public string? CustomerCode { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public CustomerType CustomerType { get; init; } = CustomerType.RetailCustomer;
    public string? TaxRegistrationNumber { get; init; }
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    public string? DefaultAddressLine1 { get; init; }
    public string? DefaultAddressLine2 { get; init; }
    public string? DefaultCity { get; init; }
    public string? DefaultStateOrProvince { get; init; }
    public string? DefaultPostalCode { get; init; }
    public string? DefaultCountryCode { get; init; }

    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }

    public int? ExpectedConfigRevision { get; init; }
}

public record SetCustomerStatusCommand(Guid Id, bool IsActive, Guid? ClientId = null, int? ExpectedConfigRevision = null)
    : IRequest<CustomerDto>;

public record AddCustomerAddressCommand : IRequest<CustomerAddressDto>
{
    public Guid CustomerId { get; init; }
    public Guid? ClientId { get; init; }
    public CustomerAddressType AddressType { get; init; } = CustomerAddressType.Delivery;
    public string? Label { get; init; }
    public string? RecipientName { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
    public string? Phone { get; init; }
    public string? DeliveryInstructions { get; init; }
    public bool IsDefault { get; init; }
}

public record UpdateCustomerAddressCommand : IRequest<CustomerAddressDto>
{
    public Guid CustomerId { get; init; }
    public Guid AddressId { get; init; }
    public Guid? ClientId { get; init; }
    public CustomerAddressType AddressType { get; init; } = CustomerAddressType.Delivery;
    public string? Label { get; init; }
    public string? RecipientName { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
    public string? Phone { get; init; }
    public string? DeliveryInstructions { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public int? ExpectedConfigRevision { get; init; }
}

public record DeleteCustomerAddressCommand(Guid CustomerId, Guid AddressId, Guid? ClientId = null) : IRequest<Unit>;

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerCode)
            .NotEmpty().MaximumLength(50)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Customer code must only contain alphanumeric characters, hyphens, or underscores.");
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(150);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
        RuleForEach(x => x.Addresses).ChildRules(a =>
        {
            a.RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(255);
            a.RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        });
    }
}

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(150);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64);
    }
}

public class AddCustomerAddressCommandValidator : AbstractValidator<AddCustomerAddressCommand>
{
    public AddCustomerAddressCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(255);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
    }
}

internal static class CustomerMapping
{
    public static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        ClientId = c.ClientId,
        CustomerCode = c.CustomerCode,
        CustomerName = c.CustomerName,
        CustomerType = c.CustomerType,
        TaxRegistrationNumber = c.TaxRegistrationNumber,
        ContactName = c.ContactName,
        Email = c.Email,
        Phone = c.Phone,
        DefaultAddressLine1 = c.DefaultAddressLine1,
        DefaultAddressLine2 = c.DefaultAddressLine2,
        DefaultCity = c.DefaultCity,
        DefaultStateOrProvince = c.DefaultStateOrProvince,
        DefaultPostalCode = c.DefaultPostalCode,
        DefaultCountryCode = c.DefaultCountryCode,
        CountryNormalizationStatus = c.CountryNormalizationStatus,
        BillingAddressLine1 = c.BillingAddressLine1,
        BillingAddressLine2 = c.BillingAddressLine2,
        BillingCity = c.BillingCity,
        BillingStateOrProvince = c.BillingStateOrProvince,
        BillingPostalCode = c.BillingPostalCode,
        BillingCountryCode = c.BillingCountryCode,
        BillingCountryNormalizationStatus = c.BillingCountryNormalizationStatus,
        IsActive = c.IsActive,
        ConfigRevision = c.ConfigRevision,
        CreatedAt = c.CreatedAt,
        Addresses = c.Addresses
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.AddressType)
            .Select(ToAddressDto)
            .ToList()
    };

    public static CustomerAddressDto ToAddressDto(CustomerAddress a) => new()
    {
        Id = a.Id,
        ClientId = a.ClientId,
        CustomerId = a.CustomerId,
        AddressType = a.AddressType,
        Label = a.Label,
        RecipientName = a.RecipientName,
        AddressLine1 = a.AddressLine1,
        AddressLine2 = a.AddressLine2,
        City = a.City,
        StateOrProvince = a.StateOrProvince,
        PostalCode = a.PostalCode,
        CountryCode = a.CountryCode,
        CountryNormalizationStatus = a.CountryNormalizationStatus,
        Phone = a.Phone,
        DeliveryInstructions = a.DeliveryInstructions,
        IsDefault = a.IsDefault,
        IsActive = a.IsActive,
        ConfigRevision = a.ConfigRevision
    };
}

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetCustomersQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<PagedResult<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.View, Permissions.Outbound.OrdersView, Permissions.Clients.View]);

        var query = _context.Customers
            .IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .AsNoTracking()
            .Where(c => c.ClientId == clientId && !c.IsDeleted);

        if (request.IsActive.HasValue)
        {
            query = query.Where(c => c.IsActive == request.IsActive.Value);
        }

        if (request.CustomerType.HasValue)
        {
            query = query.Where(c => c.CustomerType == request.CustomerType.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.CustomerName.ToLower().Contains(term) ||
                                     c.CustomerCode.ToLower().Contains(term) ||
                                     (c.Email != null && c.Email.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.CustomerName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<CustomerDto>.Create(
            items.Select(CustomerMapping.ToDto).ToList(),
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}

public class GetCustomerByIdQueryHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetCustomerByIdQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.View, Permissions.Outbound.OrdersView, Permissions.Clients.View]);

        var customer = await _context.Customers
            .IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), request.Id);

        return CustomerMapping.ToDto(customer);
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public CreateCustomerCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Create, Permissions.Outbound.OrdersManage, Permissions.Clients.Edit]);

        var client = await _context.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException("Client", clientId);

        if (client.Status != ClientStatus.Active)
        {
            throw new DomainException($"Client '{client.AccountCode}' is '{client.Status}' and cannot create new customers.", "CLIENT_INACTIVE");
        }

        var normalizedCode = Customer.NormalizeCode(request.CustomerCode);

        var exists = await _context.Customers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId &&
                           (c.NormalizedCustomerCode == normalizedCode || c.CustomerCode.ToUpper() == normalizedCode),
                cancellationToken);

        if (exists)
        {
            throw new DomainException(
                $"Customer code '{normalizedCode}' already exists for this client (including archived records).",
                "CUSTOMER_CODE_DUPLICATE");
        }

        var (defaultCountry, defaultNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.DefaultCountryCode, request.DefaultStateOrProvince, request.DefaultPostalCode, "Default", cancellationToken);

        var (billingCountry, billingNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.BillingCountryCode, request.BillingStateOrProvince, request.BillingPostalCode, "Billing", cancellationToken);

        var customer = new Customer
        {
            ClientId = clientId,
            CustomerCode = normalizedCode,
            NormalizedCustomerCode = normalizedCode,
            CustomerName = request.CustomerName.Trim(),
            CustomerType = request.CustomerType,
            TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim(),
            ContactName = request.ContactName?.Trim(),
            Email = request.Email?.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            DefaultAddressLine1 = request.DefaultAddressLine1?.Trim(),
            DefaultAddressLine2 = request.DefaultAddressLine2?.Trim(),
            DefaultCity = request.DefaultCity?.Trim(),
            DefaultStateOrProvince = request.DefaultStateOrProvince?.Trim(),
            DefaultPostalCode = request.DefaultPostalCode?.Trim(),
            DefaultCountryCode = defaultCountry,
            CountryNormalizationStatus = defaultNormStatus,
            BillingAddressLine1 = request.BillingAddressLine1?.Trim(),
            BillingAddressLine2 = request.BillingAddressLine2?.Trim(),
            BillingCity = request.BillingCity?.Trim(),
            BillingStateOrProvince = request.BillingStateOrProvince?.Trim(),
            BillingPostalCode = request.BillingPostalCode?.Trim(),
            BillingCountryCode = billingCountry,
            BillingCountryNormalizationStatus = billingNormStatus,
            IsActive = true,
            Version = 1,
            ConfigRevision = 1
        };

        foreach (var addr in request.Addresses)
        {
            var (addrCountry, addrStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
                _context, addr.CountryCode, addr.StateOrProvince, addr.PostalCode, "Address", cancellationToken);

            customer.Addresses.Add(new CustomerAddress
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                CustomerId = customer.Id,
                AddressType = addr.AddressType,
                Label = addr.Label?.Trim(),
                RecipientName = addr.RecipientName?.Trim(),
                AddressLine1 = addr.AddressLine1.Trim(),
                AddressLine2 = addr.AddressLine2?.Trim(),
                City = addr.City.Trim(),
                StateOrProvince = addr.StateOrProvince?.Trim(),
                PostalCode = addr.PostalCode?.Trim(),
                CountryCode = addrCountry,
                CountryNormalizationStatus = addrStatus,
                Phone = addr.Phone?.Trim(),
                DeliveryInstructions = addr.DeliveryInstructions?.Trim(),
                IsDefault = addr.IsDefault,
                IsActive = true,
                ConfigRevision = 1
            });
        }

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return CustomerMapping.ToDto(customer);
    }
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, CustomerDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateCustomerCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerDto> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Edit, Permissions.Outbound.OrdersManage, Permissions.Clients.Edit]);

        var customer = await _context.Customers
            .IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), request.Id);

        if (!string.IsNullOrWhiteSpace(request.CustomerCode) &&
            !string.Equals(Customer.NormalizeCode(request.CustomerCode), customer.NormalizedCustomerCode, StringComparison.Ordinal))
        {
            throw new DomainException($"Customer code '{customer.CustomerCode}' is immutable after creation.", "CUSTOMER_CODE_IMMUTABLE");
        }

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != customer.ConfigRevision)
        {
            throw new ConcurrencyException("CUSTOMER_CONCURRENCY_CONFLICT", $"Customer '{customer.CustomerCode}' was modified concurrently.");
        }

        var (defaultCountry, defaultNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.DefaultCountryCode, request.DefaultStateOrProvince, request.DefaultPostalCode, "Default", cancellationToken);

        var (billingCountry, billingNormStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.BillingCountryCode, request.BillingStateOrProvince, request.BillingPostalCode, "Billing", cancellationToken);

        customer.CustomerName = request.CustomerName.Trim();
        customer.CustomerType = request.CustomerType;
        customer.TaxRegistrationNumber = request.TaxRegistrationNumber?.Trim();
        customer.ContactName = request.ContactName?.Trim();
        customer.Email = request.Email?.Trim().ToLowerInvariant();
        customer.Phone = request.Phone?.Trim();
        customer.DefaultAddressLine1 = request.DefaultAddressLine1?.Trim();
        customer.DefaultAddressLine2 = request.DefaultAddressLine2?.Trim();
        customer.DefaultCity = request.DefaultCity?.Trim();
        customer.DefaultStateOrProvince = request.DefaultStateOrProvince?.Trim();
        customer.DefaultPostalCode = request.DefaultPostalCode?.Trim();
        customer.DefaultCountryCode = defaultCountry;
        customer.CountryNormalizationStatus = defaultNormStatus;
        customer.BillingAddressLine1 = request.BillingAddressLine1?.Trim();
        customer.BillingAddressLine2 = request.BillingAddressLine2?.Trim();
        customer.BillingCity = request.BillingCity?.Trim();
        customer.BillingStateOrProvince = request.BillingStateOrProvince?.Trim();
        customer.BillingPostalCode = request.BillingPostalCode?.Trim();
        customer.BillingCountryCode = billingCountry;
        customer.BillingCountryNormalizationStatus = billingNormStatus;
        customer.Version++;
        customer.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return CustomerMapping.ToDto(customer);
    }
}

public class SetCustomerStatusCommandHandler : IRequestHandler<SetCustomerStatusCommand, CustomerDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public SetCustomerStatusCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerDto> Handle(SetCustomerStatusCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Edit, Permissions.Customers.Delete, Permissions.Clients.Edit]);

        var customer = await _context.Customers
            .IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), request.Id);

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != customer.ConfigRevision)
        {
            throw new ConcurrencyException("CUSTOMER_CONCURRENCY_CONFLICT", $"Customer '{customer.CustomerCode}' was modified concurrently.");
        }

        // Deactivating a customer preserves all historical OutboundOrders and ShippingAddress snapshots
        // while blocking new outbound orders referencing the inactive customer.
        customer.IsActive = request.IsActive;
        customer.Version++;
        customer.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return CustomerMapping.ToDto(customer);
    }
}

public class AddCustomerAddressCommandHandler : IRequestHandler<AddCustomerAddressCommand, CustomerAddressDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public AddCustomerAddressCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerAddressDto> Handle(AddCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Edit, Permissions.Clients.Edit]);

        var customerExists = await _context.Customers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId && c.ClientId == clientId && !c.IsDeleted, cancellationToken);
        if (!customerExists)
        {
            throw new EntityNotFoundException(nameof(Customer), request.CustomerId);
        }

        var (addrCountry, addrStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.CountryCode, request.StateOrProvince, request.PostalCode, "Address", cancellationToken);

        var address = new CustomerAddress
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            CustomerId = request.CustomerId,
            AddressType = request.AddressType,
            Label = request.Label?.Trim(),
            RecipientName = request.RecipientName?.Trim(),
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City.Trim(),
            StateOrProvince = request.StateOrProvince?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            CountryCode = addrCountry,
            CountryNormalizationStatus = addrStatus,
            Phone = request.Phone?.Trim(),
            DeliveryInstructions = request.DeliveryInstructions?.Trim(),
            IsDefault = request.IsDefault,
            IsActive = true,
            ConfigRevision = 1
        };

        _context.CustomerAddresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);
        return CustomerMapping.ToAddressDto(address);
    }
}

public class UpdateCustomerAddressCommandHandler : IRequestHandler<UpdateCustomerAddressCommand, CustomerAddressDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateCustomerAddressCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<CustomerAddressDto> Handle(UpdateCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Edit, Permissions.Clients.Edit]);

        var address = await _context.CustomerAddresses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == request.CustomerId && a.ClientId == clientId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(CustomerAddress), request.AddressId);

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != address.ConfigRevision)
        {
            throw new ConcurrencyException("CUSTOMER_ADDRESS_CONCURRENCY_CONFLICT", "Customer address was modified concurrently.");
        }

        var (addrCountry, addrStatus) = await PartnerAddressValidationHelper.ValidateAndNormalizeAddressAsync(
            _context, request.CountryCode, request.StateOrProvince, request.PostalCode, "Address", cancellationToken);

        address.AddressType = request.AddressType;
        address.Label = request.Label?.Trim();
        address.RecipientName = request.RecipientName?.Trim();
        address.AddressLine1 = request.AddressLine1.Trim();
        address.AddressLine2 = request.AddressLine2?.Trim();
        address.City = request.City.Trim();
        address.StateOrProvince = request.StateOrProvince?.Trim();
        address.PostalCode = request.PostalCode?.Trim();
        address.CountryCode = addrCountry;
        address.CountryNormalizationStatus = addrStatus;
        address.Phone = request.Phone?.Trim();
        address.DeliveryInstructions = request.DeliveryInstructions?.Trim();
        address.IsDefault = request.IsDefault;
        address.IsActive = request.IsActive;
        address.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return CustomerMapping.ToAddressDto(address);
    }
}

public class DeleteCustomerAddressCommandHandler : IRequestHandler<DeleteCustomerAddressCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public DeleteCustomerAddressCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<Unit> Handle(DeleteCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Customers.Edit, Permissions.Customers.Delete, Permissions.Clients.Edit]);

        var address = await _context.CustomerAddresses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.CustomerId == request.CustomerId && a.ClientId == clientId && !a.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(CustomerAddress), request.AddressId);

        address.IsActive = false;
        address.IsDefault = false;
        address.IsDeleted = true;
        address.DeletedAt = DateTime.UtcNow;
        address.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
