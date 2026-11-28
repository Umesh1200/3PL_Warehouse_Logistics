using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients;

public record GetClientContactsQuery(Guid ClientId, bool IncludeInactive = false) : IRequest<IReadOnlyList<ClientContactDto>>;

public record CreateClientContactCommand : IRequest<ClientContactDto>
{
    public Guid ClientId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsBillingContact { get; init; }
    public bool IsPrimary { get; init; }
}

public record UpdateClientContactCommand : IRequest<ClientContactDto>
{
    public Guid ClientId { get; init; }
    public Guid ContactId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsBillingContact { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public int? ExpectedConfigRevision { get; init; }
}

public record DeleteClientContactCommand(Guid ClientId, Guid ContactId) : IRequest<Unit>;

public class CreateClientContactCommandValidator : AbstractValidator<CreateClientContactCommand>
{
    public CreateClientContactCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleTitle).MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Phone).MaximumLength(50);
    }
}

public class UpdateClientContactCommandValidator : AbstractValidator<UpdateClientContactCommand>
{
    public UpdateClientContactCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleTitle).MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Phone).MaximumLength(50);
    }
}

public class GetClientContactsQueryHandler : IRequestHandler<GetClientContactsQuery, IReadOnlyList<ClientContactDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public GetClientContactsQueryHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<IReadOnlyList<ClientContactDto>> Handle(GetClientContactsQuery request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.View]);

        var query = _context.ClientContacts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ClientId == clientId && !c.IsDeleted);

        if (!request.IncludeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync(cancellationToken);

        return items.Adapt<List<ClientContactDto>>();
    }
}

public class CreateClientContactCommandHandler : IRequestHandler<CreateClientContactCommand, ClientContactDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public CreateClientContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientContactDto> Handle(CreateClientContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.Edit]);

        var clientExists = await _context.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Id == clientId && !c.IsDeleted, cancellationToken);
        if (!clientExists)
        {
            throw new EntityNotFoundException(nameof(Client), clientId);
        }

        var contact = new ClientContact
        {
            ClientId = clientId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            RoleTitle = request.RoleTitle?.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            IsBillingContact = request.IsBillingContact,
            IsPrimary = request.IsPrimary,
            IsActive = true,
            ConfigRevision = 1
        };

        _context.ClientContacts.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);

        return contact.Adapt<ClientContactDto>();
    }
}

public class UpdateClientContactCommandHandler : IRequestHandler<UpdateClientContactCommand, ClientContactDto>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public UpdateClientContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<ClientContactDto> Handle(UpdateClientContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.Edit]);

        var contact = await _context.ClientContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClientContact), request.ContactId);

        if (request.ExpectedConfigRevision.HasValue && request.ExpectedConfigRevision.Value != contact.ConfigRevision)
        {
            throw new ConcurrencyException("CLIENT_CONTACT_CONCURRENCY_CONFLICT", "Client contact was modified concurrently.");
        }

        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.RoleTitle = request.RoleTitle?.Trim();
        contact.Email = request.Email.Trim().ToLowerInvariant();
        contact.Phone = request.Phone?.Trim();
        contact.IsBillingContact = request.IsBillingContact;
        contact.IsPrimary = request.IsPrimary;
        contact.IsActive = request.IsActive;
        contact.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return contact.Adapt<ClientContactDto>();
    }
}

public class DeleteClientContactCommandHandler : IRequestHandler<DeleteClientContactCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly PartnerMasterAuthorization _auth;

    public DeleteClientContactCommandHandler(IApplicationDbContext context, PartnerMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<Unit> Handle(DeleteClientContactCommand request, CancellationToken cancellationToken)
    {
        var (_, clientId) = await _auth.ResolveAuthorizedClientScopeAsync(
            request.ClientId,
            allowClientSelf: true,
            cancellationToken,
            allowedStaffPermissions: [Permissions.Clients.Edit]);

        var contact = await _context.ClientContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.ClientId == clientId && !c.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClientContact), request.ContactId);

        contact.IsActive = false;
        contact.IsPrimary = false;
        contact.IsDeleted = true;
        contact.DeletedAt = DateTime.UtcNow;
        contact.ConfigRevision++;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
