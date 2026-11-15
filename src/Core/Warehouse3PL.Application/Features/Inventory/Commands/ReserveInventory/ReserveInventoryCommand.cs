using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inventory.Commands.ReserveInventory;

public record ReserveInventoryCommand : IRequest<bool>
{
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid ItemId { get; init; }
    public Guid LocationId { get; init; }
    public Guid? LotId { get; init; }
    public decimal Quantity { get; init; }
    public string ReferenceType { get; init; } = "ORDER";
    public Guid? ReferenceId { get; init; }
}

public class ReserveInventoryCommandValidator : AbstractValidator<ReserveInventoryCommand>
{
    public ReserveInventoryCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class ReserveInventoryCommandHandler : IRequestHandler<ReserveInventoryCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ReserveInventoryCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(ReserveInventoryCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var stock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == request.ClientId &&
                    s.FacilityId == request.FacilityId &&
                    s.ItemId == request.ItemId &&
                    s.LocationId == request.LocationId &&
                    s.LotId == request.LotId, cancellationToken);

            if (stock == null || stock.QtyAvailable < request.Quantity)
            {
                var available = stock?.QtyAvailable ?? 0m;
                throw new ValidationException(nameof(request.Quantity),
                    $"Cannot reserve inventory. Available: {available}, Requested: {request.Quantity}.");
            }

            // State transition
            stock.QtyAvailable -= request.Quantity;
            stock.QtyReserved += request.Quantity;
            stock.Version++;

            // Record transaction
            var transaction = new InventoryTransaction
            {
                ClientId = request.ClientId,
                FacilityId = request.FacilityId,
                ItemId = request.ItemId,
                LotId = request.LotId,
                FromLocationId = request.LocationId,
                ToLocationId = request.LocationId,
                TxType = InventoryTxType.StatusChange,
                Quantity = request.Quantity,
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId,
                UserId = _currentUser.UserId,
                Notes = $"Stock reserved for {request.ReferenceType} {request.ReferenceId}",
                TxTimestamp = DateTime.UtcNow
            };

            _context.InventoryTransactions.Add(transaction);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}
