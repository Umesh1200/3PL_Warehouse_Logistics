using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;

public record CompleteReceivingSessionCommand : IRequest<ReceivingSessionDto>
{
    public Guid ReceivingSessionId { get; init; }
}

public class CompleteReceivingSessionCommandValidator : AbstractValidator<CompleteReceivingSessionCommand>
{
    public CompleteReceivingSessionCommandValidator()
    {
        RuleFor(x => x.ReceivingSessionId)
            .NotEmpty().WithMessage("Receiving session ID is required.");
    }
}

public class CompleteReceivingSessionCommandHandler : IRequestHandler<CompleteReceivingSessionCommand, ReceivingSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public CompleteReceivingSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<ReceivingSessionDto> Handle(CompleteReceivingSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.ReceivingSessions
            .Include(s => s.Facility)
            .Include(s => s.DockDoor)
            .Include(s => s.YardVisit)
            .Include(s => s.Client)
            .Include(s => s.Discrepancies)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Item)
            .Include(s => s.Lines)
                .ThenInclude(l => l.StagingLocation)
            .Include(s => s.AdvanceShippingNotice)
                .ThenInclude(asn => asn!.Lines)
            .Include(s => s.PurchaseOrder)
                .ThenInclude(po => po!.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.ReceivingSessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReceivingSession), request.ReceivingSessionId);
        }

        // 1. Enforce trusted tenant and authorized facility scope
        await _operationalAuth.ValidateReceivingSessionAccessAsync(session, cancellationToken);

        // 2. Idempotency Check: if already completed, return existing session DTO without modifying ledger
        if (session.Status == ReceivingStatus.Completed)
        {
            return MapToDto(session);
        }

        if (session.Status == ReceivingStatus.Cancelled)
        {
            throw new ValidationException(nameof(request.ReceivingSessionId), $"Cannot complete cancelled receiving session '{session.SessionNumber}'.");
        }

        // 3. Supervisor Approval Gate
        if (session.RequiresSupervisorApproval && !session.IsSupervisorApproved)
        {
            throw new ValidationException(
                nameof(request.ReceivingSessionId),
                $"Receiving session '{session.SessionNumber}' requires supervisor approval before it can be finalized (Blind receiving or flagged discrepancies).");
        }

        // 4. Pending Discrepancies Gate
        var pendingDiscrepancies = session.Discrepancies
            .Where(d => d.Status == DiscrepancyResolutionStatus.PendingReview)
            .ToList();

        if (pendingDiscrepancies.Count > 0)
        {
            throw new ValidationException(
                nameof(request.ReceivingSessionId),
                $"Cannot complete session '{session.SessionNumber}' with {pendingDiscrepancies.Count} discrepancy(ies) pending review. All discrepancies must be approved, rejected, or quarantined.");
        }

        // 5. Execute all updates atomically within a single encompassing database transaction
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var completingUserId = _currentUser.UserId ?? Guid.NewGuid();
            session.Complete(completingUserId);

            // Reconcile ASN quantities & status
            if (session.AdvanceShippingNotice != null)
            {
                foreach (var line in session.Lines)
                {
                    if (line.AsnLineItemId.HasValue)
                    {
                        session.AdvanceShippingNotice.RecordLineReceipt(line.AsnLineItemId.Value, line.QuantityReceived);
                    }
                    else
                    {
                        var matchingAsnLine = session.AdvanceShippingNotice.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
                        if (matchingAsnLine != null)
                        {
                            session.AdvanceShippingNotice.RecordLineReceipt(matchingAsnLine.Id, line.QuantityReceived);
                        }
                    }
                }

                session.AdvanceShippingNotice.CompleteReceiving();
            }

            // Reconcile PO line quantities & status
            if (session.PurchaseOrder != null)
            {
                foreach (var line in session.Lines)
                {
                    if (line.PurchaseOrderLineId.HasValue)
                    {
                        session.PurchaseOrder.RecordLineReceipt(line.PurchaseOrderLineId.Value, line.QuantityReceived);
                    }
                    else
                    {
                        var matchingPoLine = session.PurchaseOrder.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
                        if (matchingPoLine != null)
                        {
                            session.PurchaseOrder.RecordLineReceipt(matchingPoLine.Id, line.QuantityReceived);
                        }
                    }
                }
            }

            // Ledger Integration: Ingest into transient staging records
            Location? fallbackStagingLocation = null;
            var localLots = new Dictionary<(Guid ClientId, Guid ItemId, string LotNumber), InventoryLot>();
            var localStocks = new Dictionary<(Guid ClientId, Guid FacilityId, Guid ItemId, Guid LocationId, Guid? LotId), StockOnHand>();

            foreach (var line in session.Lines)
            {
                var clientId = session.ClientId ?? line.Item.ClientId;

                // Determine destination receiving staging location
                var stagingLocId = line.StagingLocationId;
                if (!stagingLocId.HasValue)
                {
                    fallbackStagingLocation ??= await _context.Locations
                        .FirstOrDefaultAsync(l =>
                            l.FacilityId == session.FacilityId &&
                            l.IsReceiving &&
                            l.LocationType == LocationType.StagingLane &&
                            l.IsActive &&
                            !l.IsDeleted &&
                            !l.IsLocked &&
                            !l.IsDamaged,
                            cancellationToken);

                    stagingLocId = fallbackStagingLocation?.Id;
                }

                if (!stagingLocId.HasValue)
                {
                    // Create a default transient receiving staging location if none exists
                    var autoStaging = new Location
                    {
                        FacilityId = session.FacilityId,
                        LocationCode = $"STAGING-RCV-{session.Facility.FacilityCode}",
                        Barcode = $"LOC-STG-RCV-{session.Facility.FacilityCode}",
                        LocationType = LocationType.StagingLane,
                        IsActive = true,
                        IsReceiving = true
                    };
                    _context.Locations.Add(autoStaging);
                    stagingLocId = autoStaging.Id;
                    fallbackStagingLocation = autoStaging;
                }

                // Persist the authoritative staging assignment onto the receipt line
                line.StagingLocationId = stagingLocId.Value;

                // Inventory Lot Handling
                InventoryLot? lot = null;
                if (!string.IsNullOrWhiteSpace(line.LotNumber))
                {
                    var normalizedLot = line.LotNumber.Trim().ToUpperInvariant();
                    var lotKey = (clientId, line.ItemId, normalizedLot);
                    if (!localLots.TryGetValue(lotKey, out lot))
                    {
                        lot = await _context.InventoryLots
                            .FirstOrDefaultAsync(l =>
                                l.ClientId == clientId &&
                                l.ItemId == line.ItemId &&
                                l.LotNumber == normalizedLot,
                                cancellationToken);

                        if (lot == null)
                        {
                            var qaStatus = (line.Condition == ReceiptConditionStatus.Damaged || line.Condition == ReceiptConditionStatus.Defective)
                                ? LotQaStatus.Quarantine
                                : LotQaStatus.Released;

                            lot = new InventoryLot
                            {
                                ClientId = clientId,
                                ItemId = line.ItemId,
                                LotNumber = normalizedLot,
                                ManufactureDate = line.ManufactureDate,
                                ExpirationDate = line.ExpirationDate,
                                QaStatus = qaStatus,
                                QaNotes = line.Condition != ReceiptConditionStatus.Sound ? $"Flagged as {line.Condition} upon intake" : null
                            };
                            _context.InventoryLots.Add(lot);
                        }

                        localLots[lotKey] = lot;
                    }
                }

                // StockOnHand snapshot update
                var lotId = lot?.Id;
                var stockKey = (clientId, session.FacilityId, line.ItemId, stagingLocId.Value, lotId);

                if (!localStocks.TryGetValue(stockKey, out var stock))
                {
                    stock = await _context.StockOnHand
                        .FirstOrDefaultAsync(s =>
                            s.ClientId == clientId &&
                            s.FacilityId == session.FacilityId &&
                            s.ItemId == line.ItemId &&
                            s.LocationId == stagingLocId.Value &&
                            s.LotId == lotId,
                            cancellationToken);

                    if (stock == null)
                    {
                        stock = new StockOnHand
                        {
                            ClientId = clientId,
                            FacilityId = session.FacilityId,
                            ItemId = line.ItemId,
                            LocationId = stagingLocId.Value,
                            LotId = lotId,
                            QtyAvailable = 0m,
                            QtyDamaged = 0m,
                            QtyQuarantined = 0m
                        };
                        _context.StockOnHand.Add(stock);
                    }

                    localStocks[stockKey] = stock;
                }

                if (line.Condition == ReceiptConditionStatus.Sound)
                {
                    stock.QtyAvailable += line.QuantityReceived;
                }
                else if (line.Condition == ReceiptConditionStatus.Damaged)
                {
                    stock.QtyDamaged += line.QuantityReceived;
                }
                else // Defective / QC Hold
                {
                    stock.QtyQuarantined += line.QuantityReceived;
                }
                stock.Version++;

                // Append-only Inventory Transaction Ledger entry
                var ledgerTx = new InventoryTransaction
                {
                    ClientId = clientId,
                    FacilityId = session.FacilityId,
                    ItemId = line.ItemId,
                    LotId = lotId,
                    FromLocationId = null,
                    ToLocationId = stagingLocId.Value,
                    TxType = InventoryTxType.Receive,
                    Quantity = line.QuantityReceived,
                    ReferenceType = "ReceivingSession",
                    ReferenceId = session.Id,
                    UserId = completingUserId,
                    TxTimestamp = DateTime.UtcNow,
                    Notes = $"Goods Received: Session {session.SessionNumber} (LPN: {line.Lpn}, Condition: {line.Condition})"
                };
                _context.InventoryTransactions.Add(ledgerTx);
            }

            // Commit all changes atomically at the end of the transaction
            await _context.SaveChangesAsync(cancellationToken);

            return MapToDto(session);
        }, cancellationToken);
    }

    private static ReceivingSessionDto MapToDto(ReceivingSession session)
    {
        return new ReceivingSessionDto
        {
            Id = session.Id,
            FacilityId = session.FacilityId,
            FacilityCode = session.Facility?.FacilityCode ?? string.Empty,
            DockDoorId = session.DockDoorId,
            DoorNumber = session.DockDoor?.DoorNumber ?? string.Empty,
            YardVisitId = session.YardVisitId,
            VisitNumber = session.YardVisit?.VisitNumber ?? string.Empty,
            TrailerNumber = session.YardVisit?.TrailerNumber ?? string.Empty,
            ClientId = session.ClientId,
            ClientName = session.Client?.CompanyName,
            AsnId = session.AsnId,
            AsnNumber = session.AdvanceShippingNotice?.AsnNumber,
            PurchaseOrderId = session.PurchaseOrderId,
            PoNumber = session.PurchaseOrder?.PoNumber,
            SessionNumber = session.SessionNumber,
            Status = session.Status,
            StartedAtUtc = session.StartedAtUtc,
            CompletedAtUtc = session.CompletedAtUtc,
            ReceivedByUserId = session.ReceivedByUserId,
            InitialProbeTemperature = session.InitialProbeTemperature,
            SealNumber = session.SealNumber,
            SealIntact = session.SealIntact,
            Notes = session.Notes,
            IsBlindReceiving = session.IsBlindReceiving,
            RequiresSupervisorApproval = session.RequiresSupervisorApproval,
            IsSupervisorApproved = session.IsSupervisorApproved,
            ApprovedByUserId = session.ApprovedByUserId,
            ApprovedAtUtc = session.ApprovedAtUtc,
            Version = session.Version,
            Lines = session.Lines.Select(l => new ReceiptLineDto
            {
                Id = l.Id,
                ReceivingSessionId = l.ReceivingSessionId,
                ItemId = l.ItemId,
                Sku = l.Item?.Sku ?? string.Empty,
                ItemDescription = l.Item?.Description ?? string.Empty,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                AsnLineItemId = l.AsnLineItemId,
                Lpn = l.Lpn,
                QuantityReceived = l.QuantityReceived,
                QuantityExpected = l.QuantityExpected,
                LotNumber = l.LotNumber,
                SerialNumber = l.SerialNumber,
                CatchWeightKg = l.CatchWeightKg,
                ManufactureDate = l.ManufactureDate,
                ExpirationDate = l.ExpirationDate,
                Condition = l.Condition,
                StagingLocationId = l.StagingLocationId,
                StagingLocationCode = l.StagingLocation?.LocationCode,
                Notes = l.Notes,
                ScannedAtUtc = l.ScannedAtUtc,
                Version = l.Version
            }).ToList(),
            Discrepancies = session.Discrepancies.Select(d => new ReceivingDiscrepancyDto
            {
                Id = d.Id,
                ReceivingSessionId = d.ReceivingSessionId,
                ReceiptLineId = d.ReceiptLineId,
                Type = d.Type,
                DiscrepantQuantity = d.DiscrepantQuantity,
                Notes = d.Notes,
                Status = d.Status,
                ReportedAtUtc = d.ReportedAtUtc,
                ResolvedAtUtc = d.ResolvedAtUtc,
                ResolvedByUserId = d.ResolvedByUserId,
                ResolutionNotes = d.ResolutionNotes,
                Version = d.Version
            }).ToList()
        };
    }
}
