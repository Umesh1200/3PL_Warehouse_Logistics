using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;

public record ScanReceiptLineCommand : IRequest<ReceiptLineDto>
{
    public Guid ReceivingSessionId { get; init; }
    public Guid ItemId { get; init; }
    public decimal QuantityReceived { get; init; }
    public string? Lpn { get; init; }
    public Guid? PurchaseOrderLineId { get; init; }
    public Guid? AsnLineItemId { get; init; }
    public string? LotNumber { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? CatchWeightKg { get; init; }
    public DateTime? ManufactureDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public ReceiptConditionStatus Condition { get; init; } = ReceiptConditionStatus.Sound;
    public Guid? StagingLocationId { get; init; }
    public string? Notes { get; init; }
}

public class ScanReceiptLineCommandValidator : AbstractValidator<ScanReceiptLineCommand>
{
    public ScanReceiptLineCommandValidator()
    {
        RuleFor(x => x.ReceivingSessionId)
            .NotEmpty().WithMessage("Receiving session ID is required.");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("Item ID is required.");

        RuleFor(x => x.QuantityReceived)
            .GreaterThan(0).WithMessage("Quantity received must be greater than zero.");
    }
}

public class ScanReceiptLineCommandHandler : IRequestHandler<ScanReceiptLineCommand, ReceiptLineDto>
{
    private readonly IApplicationDbContext _context;

    public ScanReceiptLineCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ReceiptLineDto> Handle(ScanReceiptLineCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.ReceivingSessions
            .Include(s => s.Facility)
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .Include(s => s.PurchaseOrder)
                .ThenInclude(po => po!.Lines)
            .Include(s => s.AdvanceShippingNotice)
                .ThenInclude(asn => asn!.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.ReceivingSessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingSession), request.ReceivingSessionId);
        }

        if (session.Status == ReceivingStatus.Completed || session.Status == ReceivingStatus.Cancelled)
        {
            throw new ValidationException(
                nameof(request.ReceivingSessionId),
                $"Cannot scan lines into receiving session '{session.SessionNumber}' in '{session.Status}' status.");
        }

        var item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken);

        if (item == null)
        {
            throw new EntityNotFoundException(nameof(Item), request.ItemId);
        }

        if (session.ClientId.HasValue && item.ClientId != session.ClientId.Value)
        {
            throw new ValidationException(
                nameof(request.ItemId),
                $"Item '{item.Sku}' does not belong to the client associated with this receiving session.");
        }

        decimal expectedQty = 0m;
        PurchaseOrderLine? poLine = null;
        AsnLineItem? asnLine = null;

        // 1. PO-Backed Line Matching & Over-receiving Check
        if (request.PurchaseOrderLineId.HasValue && session.PurchaseOrder != null)
        {
            poLine = session.PurchaseOrder.Lines.FirstOrDefault(l => l.Id == request.PurchaseOrderLineId.Value);
            if (poLine == null)
            {
                throw new ValidationException(nameof(request.PurchaseOrderLineId), "Referenced PO line does not exist on this session's Purchase Order.");
            }

            if (poLine.ItemId != item.Id)
            {
                throw new ValidationException(nameof(request.ItemId), "Scanned SKU does not match the referenced Purchase Order line SKU.");
            }

            expectedQty = poLine.ExpectedQty;

            var existingReceivedForPoLine = session.Lines
                .Where(l => l.PurchaseOrderLineId == poLine.Id)
                .Sum(l => l.QuantityReceived);

            var totalAfterThisScan = existingReceivedForPoLine + request.QuantityReceived;
            var maxAllowableThreshold = poLine.ExpectedQty * 1.10m; // 10% overage limit cap

            if (totalAfterThisScan > maxAllowableThreshold)
            {
                throw new ValidationException(
                    nameof(request.QuantityReceived),
                    $"Quantity exceeds allowable over-receiving threshold (10% cap). Expected: {poLine.ExpectedQty}, Total Attempted: {totalAfterThisScan}, Max Cap: {maxAllowableThreshold}.");
            }

            if (totalAfterThisScan > poLine.ExpectedQty)
            {
                var overageAmount = totalAfterThisScan - poLine.ExpectedQty;
                var overageDiscrepancy = new ReceivingDiscrepancy
                {
                    ReceivingSessionId = session.Id,
                    Type = DiscrepancyType.QuantityOverage,
                    DiscrepantQuantity = overageAmount,
                    Notes = $"Quantity overage detected on SKU '{item.Sku}'. Exceeds PO expected quantity {poLine.ExpectedQty} by {overageAmount}.",
                    Status = DiscrepancyResolutionStatus.PendingReview,
                    ReportedAtUtc = DateTime.UtcNow
                };
                session.Discrepancies.Add(overageDiscrepancy);
            }
        }
        else if (request.AsnLineItemId.HasValue && session.AdvanceShippingNotice != null)
        {
            asnLine = session.AdvanceShippingNotice.Lines.FirstOrDefault(l => l.Id == request.AsnLineItemId.Value);
            if (asnLine == null)
            {
                throw new ValidationException(nameof(request.AsnLineItemId), "Referenced ASN line does not exist on this session's ASN.");
            }

            if (asnLine.ItemId != item.Id)
            {
                throw new ValidationException(nameof(request.ItemId), "Scanned SKU does not match the referenced ASN line SKU.");
            }

            expectedQty = asnLine.ExpectedQty;
        }
        else if (session.IsBlindReceiving || (session.AsnId == null && session.PurchaseOrderId == null))
        {
            // Blind / unexpected SKU scan
            expectedQty = 0m;
            var unexpectedDiscrepancy = new ReceivingDiscrepancy
            {
                ReceivingSessionId = session.Id,
                Type = DiscrepancyType.UnexpectedSKU,
                DiscrepantQuantity = request.QuantityReceived,
                Notes = $"Blind/unexpected intake: SKU '{item.Sku}' scanned without pre-existing ASN or PO line link.",
                Status = DiscrepancyResolutionStatus.PendingReview,
                ReportedAtUtc = DateTime.UtcNow
            };
            session.Discrepancies.Add(unexpectedDiscrepancy);
            session.FlagSession($"Unexpected SKU '{item.Sku}' scanned during receiving.");
        }

        // 2. Generate LPN if not provided
        var lpn = string.IsNullOrWhiteSpace(request.Lpn)
            ? $"LPN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}"
            : request.Lpn.Trim().ToUpperInvariant();

        // 3. Staging location lookup
        var stagingLocationId = request.StagingLocationId;
        string? stagingLocationCode = null;

        if (stagingLocationId.HasValue)
        {
            var stagingLoc = await _context.Locations
                .FirstOrDefaultAsync(l => l.Id == stagingLocationId.Value && l.FacilityId == session.FacilityId, cancellationToken);

            if (stagingLoc != null)
            {
                stagingLocationCode = stagingLoc.LocationCode;
            }
        }
        else
        {
            var defaultStaging = await _context.Locations
                .FirstOrDefaultAsync(l => l.FacilityId == session.FacilityId && l.IsReceiving && l.IsActive, cancellationToken);

            if (defaultStaging != null)
            {
                stagingLocationId = defaultStaging.Id;
                stagingLocationCode = defaultStaging.LocationCode;
            }
        }

        var receiptLine = new ReceiptLine
        {
            ReceivingSessionId = session.Id,
            ItemId = item.Id,
            PurchaseOrderLineId = poLine?.Id,
            AsnLineItemId = asnLine?.Id,
            Lpn = lpn,
            QuantityReceived = request.QuantityReceived,
            QuantityExpected = expectedQty,
            LotNumber = request.LotNumber?.Trim().ToUpperInvariant(),
            SerialNumber = request.SerialNumber?.Trim(),
            CatchWeightKg = request.CatchWeightKg,
            ManufactureDate = request.ManufactureDate,
            ExpirationDate = request.ExpirationDate,
            Condition = request.Condition,
            StagingLocationId = stagingLocationId,
            Notes = request.Notes,
            ScannedAtUtc = DateTime.UtcNow
        };

        // 4. Damage & Defect Auto-Routing to Quarantine Discrepancy
        if (request.Condition == ReceiptConditionStatus.Damaged || request.Condition == ReceiptConditionStatus.Defective)
        {
            var damageDiscrepancy = new ReceivingDiscrepancy
            {
                ReceivingSessionId = session.Id,
                ReceiptLineId = receiptLine.Id,
                Type = DiscrepancyType.DamagedGoods,
                DiscrepantQuantity = request.QuantityReceived,
                Notes = $"Condition flagged as '{request.Condition}' on LPN '{lpn}' (SKU: {item.Sku}): {request.Notes ?? "Physical defect or container damage reported upon scan."}",
                Status = DiscrepancyResolutionStatus.Quarantined,
                ReportedAtUtc = DateTime.UtcNow
            };
            session.Discrepancies.Add(damageDiscrepancy);
        }

        if (session.Status == ReceivingStatus.Started)
        {
            session.Status = ReceivingStatus.InProgress;
        }

        session.Lines.Add(receiptLine);
        await _context.SaveChangesAsync(cancellationToken);

        return new ReceiptLineDto
        {
            Id = receiptLine.Id,
            ReceivingSessionId = receiptLine.ReceivingSessionId,
            ItemId = receiptLine.ItemId,
            Sku = item.Sku,
            ItemDescription = item.Description,
            PurchaseOrderLineId = receiptLine.PurchaseOrderLineId,
            AsnLineItemId = receiptLine.AsnLineItemId,
            Lpn = receiptLine.Lpn,
            QuantityReceived = receiptLine.QuantityReceived,
            QuantityExpected = receiptLine.QuantityExpected,
            LotNumber = receiptLine.LotNumber,
            SerialNumber = receiptLine.SerialNumber,
            CatchWeightKg = receiptLine.CatchWeightKg,
            ManufactureDate = receiptLine.ManufactureDate,
            ExpirationDate = receiptLine.ExpirationDate,
            Condition = receiptLine.Condition,
            StagingLocationId = receiptLine.StagingLocationId,
            StagingLocationCode = stagingLocationCode,
            Notes = receiptLine.Notes,
            ScannedAtUtc = receiptLine.ScannedAtUtc,
            Version = receiptLine.Version
        };
    }
}
