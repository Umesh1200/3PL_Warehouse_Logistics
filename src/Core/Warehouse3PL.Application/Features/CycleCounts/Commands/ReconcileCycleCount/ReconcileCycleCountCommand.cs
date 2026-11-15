using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CycleCounts.Commands.ReconcileCycleCount;

public record ReconcileCycleCountCommand(
    Guid CycleCountId,
    AdjustmentReasonCode DefaultReasonCode = AdjustmentReasonCode.Miscount,
    string? Notes = null) : IRequest<CycleCountDto>;

public class ReconcileCycleCountCommandValidator : AbstractValidator<ReconcileCycleCountCommand>
{
    public ReconcileCycleCountCommandValidator()
    {
        RuleFor(x => x.CycleCountId).NotEmpty();
    }
}

public class ReconcileCycleCountCommandHandler : IRequestHandler<ReconcileCycleCountCommand, CycleCountDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ReconcileCycleCountCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CycleCountDto> Handle(ReconcileCycleCountCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var count = await _context.CycleCounts
                .Include(cc => cc.Items)
                    .ThenInclude(cci => cci.Item)
                .Include(cc => cc.Items)
                    .ThenInclude(cci => cci.Location)
                .Include(cc => cc.Items)
                    .ThenInclude(cci => cci.Lot)
                .FirstOrDefaultAsync(cc => cc.Id == request.CycleCountId, cancellationToken);

            if (count == null)
            {
                throw new EntityNotFoundException(nameof(CycleCount), request.CycleCountId);
            }

            var discrepancies = 0;

            foreach (var countItem in count.Items.Where(i => i.CountedQty.HasValue && !i.IsReconciled))
            {
                var variance = countItem.CountedQty!.Value - countItem.ExpectedQty;

                if (variance != 0m)
                {
                    discrepancies++;

                    // Find corresponding stock on hand record
                    var stock = await _context.StockOnHand
                        .FirstOrDefaultAsync(s =>
                            s.FacilityId == count.FacilityId &&
                            s.LocationId == countItem.LocationId &&
                            s.ItemId == countItem.ItemId &&
                            s.LotId == countItem.LotId, cancellationToken);

                    if (stock != null)
                    {
                        stock.QtyAvailable = Math.Max(0, stock.QtyAvailable + variance);
                        stock.LastCountedAt = DateTime.UtcNow;
                        stock.Version++;

                        var reason = variance > 0 ? AdjustmentReasonCode.FoundStock : request.DefaultReasonCode;
                        var adj = new InventoryAdjustment
                        {
                            ClientId = stock.ClientId,
                            FacilityId = count.FacilityId,
                            StockId = stock.Id,
                            ItemId = countItem.ItemId,
                            LocationId = countItem.LocationId,
                            LotId = countItem.LotId,
                            QtyAdjusted = variance,
                            ReasonCode = reason,
                            ReasonNotes = request.Notes ?? $"Cycle count reconciliation for count {count.CountNumber}",
                            ApprovedByUserId = _currentUser.UserId,
                            AccountingImpactAmount = variance * countItem.Item.UnitPrice,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.InventoryAdjustments.Add(adj);

                        var tx = new InventoryTransaction
                        {
                            ClientId = stock.ClientId,
                            FacilityId = count.FacilityId,
                            ItemId = countItem.ItemId,
                            LotId = countItem.LotId,
                            FromLocationId = countItem.LocationId,
                            ToLocationId = countItem.LocationId,
                            TxType = InventoryTxType.CycleCount,
                            Quantity = Math.Abs(variance),
                            ReferenceType = "CYCLE_COUNT",
                            ReferenceId = count.Id,
                            UserId = _currentUser.UserId,
                            Notes = $"Variance adjustment of {variance:+#.##;-#.##;0} units",
                            TxTimestamp = DateTime.UtcNow
                        };
                        _context.InventoryTransactions.Add(tx);
                    }
                }

                countItem.IsReconciled = true;
                countItem.ApprovedByUserId = _currentUser.UserId;
                countItem.ApprovedAt = DateTime.UtcNow;
            }

            count.Status = CycleCountStatus.Completed;
            count.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new CycleCountDto
            {
                Id = count.Id,
                FacilityId = count.FacilityId,
                ZoneId = count.ZoneId,
                CountNumber = count.CountNumber,
                CountType = count.CountType,
                Status = count.Status,
                ScheduledDate = count.ScheduledDate,
                StartedAt = count.StartedAt,
                CompletedAt = count.CompletedAt,
                Notes = count.Notes,
                TotalItemsCount = count.Items.Count,
                DiscrepancyCount = discrepancies,
                Items = count.Items.Select(i => new CycleCountItemDto
                {
                    Id = i.Id,
                    LocationId = i.LocationId,
                    LocationCode = i.Location.LocationCode,
                    ItemId = i.ItemId,
                    Sku = i.Item.Sku,
                    LotId = i.LotId,
                    LotNumber = i.Lot?.LotNumber,
                    ExpectedQty = i.ExpectedQty,
                    CountedQty = i.CountedQty,
                    VarianceQty = i.VarianceQty,
                    IsReconciled = i.IsReconciled,
                    CountedAt = i.CountedAt
                }).ToList()
            };
        }, cancellationToken);
    }
}

