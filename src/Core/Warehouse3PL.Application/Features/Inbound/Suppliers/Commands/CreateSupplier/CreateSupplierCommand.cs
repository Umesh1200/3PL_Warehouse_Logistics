using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;

public record CreateSupplierCommand : IRequest<SupplierDto>
{
    public Guid? ClientId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.SupplierCode)
            .NotEmpty().WithMessage("Supplier code is required.")
            .MaximumLength(50).WithMessage("Supplier code must not exceed 50 characters.");

        RuleFor(x => x.SupplierName)
            .NotEmpty().WithMessage("Supplier name is required.")
            .MaximumLength(200).WithMessage("Supplier name must not exceed 200 characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("Invalid email format.");
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, SupplierDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CreateSupplierCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var targetClientId = _currentUser.IsStaff && request.ClientId.HasValue
            ? request.ClientId.Value
            : _currentUser.TenantId ?? request.ClientId ?? Guid.Empty;

        if (targetClientId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ClientId), "A valid ClientId is required to register a supplier.");
        }

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == targetClientId, cancellationToken);
        if (!clientExists)
        {
            throw new EntityNotFoundException(nameof(Client), targetClientId);
        }

        var codeExists = await _context.Suppliers
            .AnyAsync(s => s.ClientId == targetClientId && s.SupplierCode == request.SupplierCode.Trim(), cancellationToken);

        if (codeExists)
        {
            throw new ValidationException(nameof(request.SupplierCode), $"Supplier code '{request.SupplierCode}' already exists for this client.");
        }

        var supplier = new Supplier
        {
            ClientId = targetClientId,
            SupplierCode = request.SupplierCode.Trim().ToUpperInvariant(),
            SupplierName = request.SupplierName.Trim(),
            ContactName = request.ContactName?.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            AddressLine1 = request.AddressLine1?.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            Country = request.Country?.Trim(),
            IsActive = true
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);

        return new SupplierDto
        {
            Id = supplier.Id,
            ClientId = supplier.ClientId,
            SupplierCode = supplier.SupplierCode,
            SupplierName = supplier.SupplierName,
            ContactName = supplier.ContactName,
            Email = supplier.Email,
            Phone = supplier.Phone,
            AddressLine1 = supplier.AddressLine1,
            AddressLine2 = supplier.AddressLine2,
            City = supplier.City,
            State = supplier.State,
            PostalCode = supplier.PostalCode,
            Country = supplier.Country,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt
        };
    }
}

