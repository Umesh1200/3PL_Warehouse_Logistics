using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;

public record TransferInventoryCommand : IRequest<long>
{
    public Guid ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid ItemId { get; init; }
    public Guid? LotId { get; init; }
    public Guid FromLocationId { get; init; }
    public Guid ToLocationId { get; init; }
    public decimal Quantity { get; init; }
    public string? Reason { get; init; }
}

public class TransferInventoryCommandValidator : AbstractValidator<TransferInventoryCommand>
{
    public TransferInventoryCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.FacilityId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.FromLocationId).NotEmpty();
        RuleFor(x => x.ToLocationId).NotEmpty().NotEqual(x => x.FromLocationId).WithMessage("Source and destination locations cannot be identical.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Transfer quantity must be greater than zero.");
    }
}

public class TransferInventoryCommandHandler : IRequestHandler<TransferInventoryCommand, long>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public TransferInventoryCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<long> Handle(TransferInventoryCommand request, CancellationToken cancellationToken)
    {
        await _operationalAuth.ValidateClientFacilityAccessAsync(
            request.ClientId, request.FacilityId, cancellationToken);

        return await _context.ExecuteInTransactionAsync(async () =>
        {
            // 1. Fetch Source Stock Balance
            var sourceStock = await _context.StockOnHand
                .Include(s => s.Item)
                .FirstOrDefaultAsync(s =>
                    s.ClientId == request.ClientId &&
                    s.FacilityId == request.FacilityId &&
                    s.ItemId == request.ItemId &&
                    s.LocationId == request.FromLocationId &&
                    s.LotId == request.LotId, cancellationToken);

            if (sourceStock == null || sourceStock.QtyAvailable < request.Quantity)
            {
                var available = sourceStock?.QtyAvailable ?? 0m;
                throw new ValidationException(nameof(request.Quantity),
                    $"Insufficient available stock at source location. Requested: {request.Quantity}, Available: {available}.");
            }

            // 2. Acquire canonical LocationLifecycle + StockOnHand locks (MDM21-R04) and validate destination
            await using var moveLocks = await _context.AcquirePutawayExecutionLocksAsync(
                sourceStock.Id, request.ToLocationId, cancellationToken);

            var destLocation = await _context.Locations
                .AsNoTracking()
                .Include(l => l.Zone)
                .Include(l => l.StorageProfile)
                .FirstOrDefaultAsync(l => l.Id == request.ToLocationId && l.FacilityId == request.FacilityId && !l.IsDeleted, cancellationToken);

            if (destLocation == null)
            {
                throw new EntityNotFoundException(nameof(Location), request.ToLocationId);
            }

            if (destLocation.IsReceiving || destLocation.LocationType == LocationType.StagingLane)
            {
                throw new ValidationException(nameof(request.ToLocationId),
                    $"Target location '{destLocation.LocationCode}' is a staging/receiving location, not a permanent storage location.");
            }

            // MDM21-R03: Enforce IsStorable, LocationStatus, Zone.DedicatedClientId, LocationStorageProfile, and thermal/capacity limits
            var item = sourceStock.Item ?? await _context.Items
                .AsNoTracking()
                .FirstAsync(i => i.Id == request.ItemId, cancellationToken);

            await Putaway.PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(
                _context, destLocation, item, request.Quantity, cancellationToken, request.LotId);

            // 3. Atomically Decrement Source Balance
            sourceStock.QtyAvailable -= request.Quantity;
            sourceStock.Version++;

            // 4. Increment Destination Balance (or insert if non-existent)
            var destStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == request.ClientId &&
                    s.FacilityId == request.FacilityId &&
                    s.ItemId == request.ItemId &&
                    s.LocationId == request.ToLocationId &&
                    s.LotId == request.LotId, cancellationToken);

            if (destStock == null)
            {
                destStock = new StockOnHand
                {
                    ClientId = request.ClientId,
                    FacilityId = request.FacilityId,
                    ItemId = request.ItemId,
                    LocationId = request.ToLocationId,
                    LotId = request.LotId,
                    QtyAvailable = request.Quantity,
                    Version = 1
                };
                _context.StockOnHand.Add(destStock);
            }
            else
            {
                destStock.QtyAvailable += request.Quantity;
                destStock.Version++;
            }

            // 5. Append Immutable Ledger Entry
            var transaction = new InventoryTransaction
            {
                ClientId = request.ClientId,
                FacilityId = request.FacilityId,
                ItemId = request.ItemId,
                LotId = request.LotId,
                FromLocationId = request.FromLocationId,
                ToLocationId = request.ToLocationId,
                TxType = InventoryTxType.Move,
                Quantity = request.Quantity,
                ReferenceType = "MANUAL_TRANSFER",
                UserId = _currentUser.UserId,
                Notes = request.Reason ?? "Relocation between warehouse storage bins",
                TxTimestamp = DateTime.UtcNow
            };

            _context.InventoryTransactions.Add(transaction);

            await _context.SaveChangesAsync(cancellationToken);
            return transaction.Id;
        }, cancellationToken);
    }
}
