using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;

public record UpdateClientCommand : IRequest<ClientDto>
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public ClientStatus Status { get; init; }
    public SlaTier SlaTier { get; init; }
    public string Currency { get; init; } = "USD";
}

public class UpdateClientCommandValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Client ID is required.");
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public class UpdateClientCommandHandler : IRequestHandler<UpdateClientCommand, ClientDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public UpdateClientCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<ClientDto> Handle(UpdateClientCommand request, CancellationToken cancellationToken)
    {
        // Enforce tenant boundary for non-staff; fail-closed without tenant
        if (!_currentUser.IsStaff)
        {
            if (!_currentUser.TenantId.HasValue)
            {
                throw new ForbiddenException("Non-staff users without an assigned tenant context are forbidden from updating client records.");
            }
            if (_currentUser.TenantId.Value != request.Id)
            {
                throw new ForbiddenException("You cannot update details for another client tenant.");
            }
        }

        var client = await _context.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken);

        if (client == null)
        {
            throw new EntityNotFoundException(nameof(Client), request.Id);
        }

        client.CompanyName = request.CompanyName.Trim();
        client.Status = request.Status;
        client.SlaTier = request.SlaTier;
        client.Currency = request.Currency.Trim().ToUpperInvariant();

        await _context.SaveChangesAsync(cancellationToken);

        return client.Adapt<ClientDto>();
    }
}

