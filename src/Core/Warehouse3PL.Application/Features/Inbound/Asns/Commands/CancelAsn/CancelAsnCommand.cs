using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Asns.Commands.CancelAsn;

public record CancelAsnCommand(Guid Id, string Reason) : IRequest<bool>;

public class CancelAsnCommandValidator : AbstractValidator<CancelAsnCommand>
{
    public CancelAsnCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .MaximumLength(500).WithMessage("Cancellation reason must not exceed 500 characters.");
    }
}

public class CancelAsnCommandHandler : IRequestHandler<CancelAsnCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CancelAsnCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(CancelAsnCommand request, CancellationToken cancellationToken)
    {
        var asn = await _context.AdvanceShippingNotices
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (asn == null)
        {
            throw new EntityNotFoundException(nameof(AdvanceShippingNotice), request.Id);
        }

        if (!_currentUser.IsStaff && asn.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have access to this ASN.");
        }

        asn.Cancel(request.Reason.Trim());

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

