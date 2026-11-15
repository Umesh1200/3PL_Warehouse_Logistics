using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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

    // Optional primary contact details
    public string? ContactFirstName { get; init; }
    public string? ContactLastName { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
}

public class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200);

        RuleFor(x => x.AccountCode)
            .NotEmpty().WithMessage("Account code is required.")
            .MinimumLength(3).WithMessage("Account code must be at least 3 characters.")
            .MaximumLength(50)
            .Matches(@"^[A-Z0-9_\-]+$").WithMessage("Account code must contain uppercase alphanumeric characters, dashes, or underscores only.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-character ISO code.");

        When(x => !string.IsNullOrEmpty(x.ContactEmail), () =>
        {
            RuleFor(x => x.ContactEmail)
                .EmailAddress().WithMessage("Contact email must be a valid email.");
        });
    }
}

public class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _context;

    public CreateClientCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClientDto> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        var normalizedAccountCode = request.AccountCode.Trim().ToUpperInvariant();

        var existing = await _context.Clients
            .AnyAsync(c => c.AccountCode == normalizedAccountCode && !c.IsDeleted, cancellationToken);

        if (existing)
        {
            throw new ValidationException(nameof(request.AccountCode), $"A client with account code '{normalizedAccountCode}' already exists.");
        }

        var client = new Client
        {
            CompanyName = request.CompanyName.Trim(),
            AccountCode = normalizedAccountCode,
            Status = request.Status,
            SlaTier = request.SlaTier,
            Currency = request.Currency.Trim().ToUpperInvariant()
        };

        if (!string.IsNullOrWhiteSpace(request.ContactFirstName) && !string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            client.Contacts.Add(new ClientContact
            {
                Client = client,
                FirstName = request.ContactFirstName.Trim(),
                LastName = request.ContactLastName?.Trim() ?? string.Empty,
                Email = request.ContactEmail.Trim().ToLower(),
                Phone = request.ContactPhone,
                IsBillingContact = true
            });
        }

        _context.Clients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);

        return client.Adapt<ClientDto>();
    }
}
