using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Inspections.Commands.RecordInspectionDecision;

public record RecordInspectionDecisionCommand : IRequest<InspectionDecisionDto>
{
    public Guid DiscrepancyId { get; init; }
    public DiscrepancyResolutionStatus Decision { get; init; }
    public string? ResolutionNotes { get; init; }
}

public class RecordInspectionDecisionCommandValidator : AbstractValidator<RecordInspectionDecisionCommand>
{
    public RecordInspectionDecisionCommandValidator()
    {
        RuleFor(x => x.DiscrepancyId)
            .NotEmpty().WithMessage("Discrepancy ID is required.");

        RuleFor(x => x.Decision)
            .IsInEnum().WithMessage("Valid resolution status is required.");
    }
}

public class RecordInspectionDecisionCommandHandler : IRequestHandler<RecordInspectionDecisionCommand, InspectionDecisionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public RecordInspectionDecisionCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<InspectionDecisionDto> Handle(RecordInspectionDecisionCommand request, CancellationToken cancellationToken)
    {
        var discrepancy = await _context.ReceivingDiscrepancies
            .Include(d => d.ReceiptLine)
                .ThenInclude(l => l!.Item)
            .Include(d => d.ReceivingSession)
                .ThenInclude(s => s.Discrepancies)
            .FirstOrDefaultAsync(d => d.Id == request.DiscrepancyId, cancellationToken);

        if (discrepancy == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingDiscrepancy), request.DiscrepancyId);
        }

        var supervisorId = _currentUser.UserId ?? Guid.NewGuid();
        discrepancy.Resolve(request.Decision, supervisorId, request.ResolutionNotes);

        decimal releasedQty = 0m;
        decimal quarantinedQty = 0m;

        // If Approved, release associated quarantined stock to Available in Ledger
        if (request.Decision == DiscrepancyResolutionStatus.Approved && discrepancy.ReceiptLine != null)
        {
            var line = discrepancy.ReceiptLine;
            var session = discrepancy.ReceivingSession;
            var clientId = session.ClientId ?? line.Item.ClientId;

            var stock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == clientId &&
                    s.FacilityId == session.FacilityId &&
                    s.ItemId == line.ItemId &&
                    s.LocationId == line.StagingLocationId,
                    cancellationToken);

            if (stock != null)
            {
                var qtyToRelease = Math.Min(stock.QtyQuarantined, discrepancy.DiscrepantQuantity);
                if (qtyToRelease > 0)
                {
                    stock.QtyQuarantined -= qtyToRelease;
                    stock.QtyAvailable += qtyToRelease;
                    stock.Version++;
                    releasedQty = qtyToRelease;

                    var ledgerTx = new InventoryTransaction
                    {
                        ClientId = clientId,
                        FacilityId = session.FacilityId,
                        ItemId = line.ItemId,
                        LotId = stock.LotId,
                        FromLocationId = line.StagingLocationId,
                        ToLocationId = line.StagingLocationId,
                        TxType = InventoryTxType.StatusChange,
                        Quantity = qtyToRelease,
                        ReferenceType = "QualityInspection",
                        ReferenceId = discrepancy.Id,
                        UserId = supervisorId,
                        TxTimestamp = DateTime.UtcNow,
                        Notes = $"QC Inspection Approved: Released {qtyToRelease} from quarantine to available for LPN {line.Lpn}"
                    };
                    _context.InventoryTransactions.Add(ledgerTx);
                }
            }

            // Update lot status if lot exists
            if (!string.IsNullOrWhiteSpace(line.LotNumber))
            {
                var lot = await _context.InventoryLots
                    .FirstOrDefaultAsync(l =>
                        l.ClientId == clientId &&
                        l.ItemId == line.ItemId &&
                        l.LotNumber == line.LotNumber,
                        cancellationToken);

                if (lot != null)
                {
                    lot.QaStatus = LotQaStatus.Released;
                    lot.QaNotes = $"Released by supervisor inspection on {DateTime.UtcNow:yyyy-MM-dd}";
                }
            }
        }
        else
        {
            quarantinedQty = discrepancy.DiscrepantQuantity;
        }

        // Check if supervisor approval for session can now be marked
        var sessionToApprove = discrepancy.ReceivingSession;
        var pendingReviewRemaining = sessionToApprove.Discrepancies.Any(d => d.Status == DiscrepancyResolutionStatus.PendingReview);
        if (!pendingReviewRemaining)
        {
            sessionToApprove.ApproveSupervisor(supervisorId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new InspectionDecisionDto
        {
            DiscrepancyId = discrepancy.Id,
            Status = discrepancy.Status,
            ResolvedByUserId = supervisorId,
            ResolvedAtUtc = discrepancy.ResolvedAtUtc ?? DateTime.UtcNow,
            ResolutionNotes = discrepancy.ResolutionNotes,
            ReleasedQuantity = releasedQty,
            QuarantinedQuantity = quarantinedQty
        };
    }
}

