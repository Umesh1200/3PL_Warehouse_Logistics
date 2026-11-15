using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CancelPurchaseOrder;

public record CancelPurchaseOrderCommand(Guid Id, string Reason) : IRequest<bool>;

public class CancelPurchaseOrderCommandValidator : AbstractValidator<CancelPurchaseOrderCommand>
{
    public CancelPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .MaximumLength(500).WithMessage("Cancellation reason must not exceed 500 characters.");
    }
}

public class CancelPurchaseOrderCommandHandler : IRequestHandler<CancelPurchaseOrderCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CancelPurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (po == null)
        {
            throw new EntityNotFoundException(nameof(PurchaseOrder), request.Id);
        }

        if (!_currentUser.IsStaff && po.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have access to this purchase order.");
        }

        po.Cancel(request.Reason.Trim());

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

