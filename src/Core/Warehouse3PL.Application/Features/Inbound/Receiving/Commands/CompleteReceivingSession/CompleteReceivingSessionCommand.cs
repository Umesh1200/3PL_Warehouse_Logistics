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

        // 5. Execute all lifecycle locking, destination validation, and ledger updates atomically inside a single transaction (MDM21-F03-R01-B)
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            await using var sessionLock = await _context.AcquireScopedIdempotencyLockAsync(
                "RCV_COMPLETE", session.FacilityId, request.ReceivingSessionId.ToString("N"), cancellationToken);

            // Re-read authoritative session inside transaction
            var lockedSession = await _context.ReceivingSessions
                .Include(s => s.Facility)
                .Include(s => s.DockDoor)
                .Include(s => s.YardVisit)
                .Include(s => s.Client)
                .Include(s => s.Discrepancies)
                .Include(s => s.Lines)
                    .ThenInclude(l => l.Item)
                .Include(s => s.AdvanceShippingNotice)
                    .ThenInclude(asn => asn!.Lines)
                .Include(s => s.PurchaseOrder)
                    .ThenInclude(po => po!.Lines)
                .FirstOrDefaultAsync(s => s.Id == request.ReceivingSessionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(ReceivingSession), request.ReceivingSessionId);

            if (lockedSession.Status == ReceivingStatus.Completed)
            {
                return MapToDto(lockedSession);
            }

            if (lockedSession.Status == ReceivingStatus.Cancelled)
            {
                throw new ValidationException(nameof(request.ReceivingSessionId), $"Cannot complete cancelled receiving session '{lockedSession.SessionNumber}'.");
            }

            // Resolve authoritative staging destinations for all lines
            var resolvedLineTargets = new Dictionary<Guid, Guid>();
            Location? fallbackStagingLocation = null;

            foreach (var line in lockedSession.Lines)
            {
                var lineClientId = lockedSession.ClientId ?? line.Item.ClientId;
                var stagingLocId = line.StagingLocationId;

                if (!stagingLocId.HasValue)
                {
                    if (fallbackStagingLocation == null)
                    {
                        var candidates = await _context.Locations
                            .AsNoTracking()
                            .Include(l => l.Zone)
                            .Include(l => l.StorageProfile)
                            .Where(l =>
                                l.FacilityId == lockedSession.FacilityId &&
                                l.IsReceiving &&
                                l.LocationType == LocationType.StagingLane &&
                                l.IsActive &&
                                l.Status == LocationStatus.Active &&
                                !l.IsDeleted &&
                                !l.IsLocked &&
                                !l.IsDamaged)
                            .OrderBy(l => l.LocationCode)
                            .ToListAsync(cancellationToken);

                        fallbackStagingLocation = candidates.FirstOrDefault(l =>
                            (l.Zone == null || (l.Zone.IsActive && (!l.Zone.DedicatedClientId.HasValue || l.Zone.DedicatedClientId.Value == lineClientId))) &&
                            (l.StorageProfile == null || !l.StorageProfile.DedicatedClientId.HasValue || l.StorageProfile.DedicatedClientId.Value == lineClientId));
                    }

                    if (fallbackStagingLocation == null)
                    {
                        // MDM21-F03-R01-B: Safe automatic staging provisioning under protected FacilityHierarchy lock,
                        // enforcing active facility, hierarchy rules, derived hierarchy metadata, and permanent normalized uniqueness.
                        fallbackStagingLocation = await _context.ExecuteWithFacilityHierarchyLockAsync(lockedSession.FacilityId, async () =>
                        {
                            await WarehouseMaster.WarehouseInventoryDependencyGuard.EnsureFacilityIsActiveAsync(
                                _context, lockedSession.FacilityId, cancellationToken);

                            await WarehouseMaster.LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                                _context,
                                lockedSession.FacilityId,
                                null,
                                LocationType.StagingLane,
                                targetHierarchyLevel: 1,
                                cancellationToken,
                                requestedIsStorable: true);

                            var facilityCode = lockedSession.Facility?.FacilityCode ?? lockedSession.FacilityId.ToString("N")[..8].ToUpperInvariant();
                            var baseCode = $"STAGING-RCV-{facilityCode}";
                            var baseBarcode = $"LOC-STG-RCV-{facilityCode}";
                            var candidateCode = baseCode;
                            var candidateBarcode = baseBarcode;
                            int suffix = 0;

                            while (await _context.Locations.IgnoreQueryFilters().AsNoTracking().AnyAsync(l =>
                                l.FacilityId == lockedSession.FacilityId &&
                                (l.NormalizedLocationCode == Location.NormalizeCode(candidateCode) ||
                                 l.NormalizedBarcode == Location.NormalizeCode(candidateBarcode)),
                                cancellationToken))
                            {
                                suffix++;
                                candidateCode = $"{baseCode}-{suffix}";
                                candidateBarcode = $"{baseBarcode}-{suffix}";
                            }

                            var autoId = Guid.NewGuid();
                            var autoStaging = new Location
                            {
                                Id = autoId,
                                FacilityId = lockedSession.FacilityId,
                                LocationType = LocationType.StagingLane,
                                LocationCode = candidateCode,
                                NormalizedLocationCode = Location.NormalizeCode(candidateCode),
                                LocationName = candidateCode,
                                Barcode = candidateBarcode,
                                NormalizedBarcode = Location.NormalizeCode(candidateBarcode),
                                HierarchyLevel = 1,
                                HierarchyPath = Location.BuildHierarchyPath(null, autoId),
                                Status = LocationStatus.Active,
                                IsActive = true,
                                IsStorable = true,
                                IsReceiving = true,
                                ConfigRevision = 1,
                                CreatedBy = _currentUser.UserId
                            };
                            _context.Locations.Add(autoStaging);
                            await _context.SaveChangesAsync(cancellationToken);
                            return autoStaging;
                        }, cancellationToken);
                    }

                    stagingLocId = fallbackStagingLocation.Id;
                }

                resolvedLineTargets[line.Id] = stagingLocId.Value;
            }

            // Acquire deterministic FACILITY_LIFECYCLE(SH) -> sorted ZONE_LIFECYCLE(SH) -> sorted LOCATION_LIFECYCLE(EX)
            var distinctStagingIds = resolvedLineTargets.Values.Distinct().OrderBy(id => id).ToList();
            await using var stagingLocks = await _context.AcquireMultiLocationExecutionLocksAsync(
                lockedSession.FacilityId, distinctStagingIds, cancellationToken);

            // Re-validate active facility under lock
            await WarehouseMaster.WarehouseInventoryDependencyGuard.EnsureFacilityIsActiveAsync(
                _context, lockedSession.FacilityId, cancellationToken);

            // Re-read all staging destinations under lock and validate EVERY line BEFORE mutating ASN/PO/Stock/Ledger
            var lockedLocations = await _context.Locations
                .AsNoTracking()
                .Include(l => l.Zone)
                .Include(l => l.StorageProfile)
                .Where(l => distinctStagingIds.Contains(l.Id) && !l.IsDeleted)
                .ToDictionaryAsync(l => l.Id, cancellationToken);

            foreach (var line in lockedSession.Lines)
            {
                var lineClientId = lockedSession.ClientId ?? line.Item.ClientId;
                var targetId = resolvedLineTargets[line.Id];

                if (!lockedLocations.TryGetValue(targetId, out var destLoc) || destLoc.FacilityId != lockedSession.FacilityId)
                {
                    throw new ValidationException(
                        nameof(line.StagingLocationId),
                        $"Receiving staging location '{targetId}' does not exist in facility '{lockedSession.FacilityId}'.");
                }

                if (!destLoc.IsActive || destLoc.Status != LocationStatus.Active)
                {
                    throw new ValidationException(
                        nameof(line.StagingLocationId),
                        $"Receiving staging location '{destLoc.LocationCode}' is inactive or decommissioned (status: '{destLoc.Status}').");
                }

                if (destLoc.IsLocked || destLoc.IsDamaged)
                {
                    throw new ValidationException(
                        nameof(line.StagingLocationId),
                        $"Receiving staging location '{destLoc.LocationCode}' is locked or damaged and cannot accept received inventory.");
                }

                if (destLoc.LocationType != LocationType.StagingLane || !destLoc.IsReceiving)
                {
                    throw new ValidationException(
                        nameof(line.StagingLocationId),
                        $"Location '{destLoc.LocationCode}' (type '{destLoc.LocationType}', isReceiving: {destLoc.IsReceiving}) is not an eligible receiving staging lane.");
                }

                if (destLoc.Zone != null)
                {
                    if (!destLoc.Zone.IsActive)
                    {
                        throw new ValidationException(
                            nameof(line.StagingLocationId),
                            $"Receiving staging zone '{destLoc.Zone.ZoneCode}' is inactive.");
                    }

                    if (destLoc.Zone.DedicatedClientId.HasValue && destLoc.Zone.DedicatedClientId.Value != lineClientId)
                    {
                        throw new ValidationException(
                            nameof(line.StagingLocationId),
                            $"Receiving staging zone '{destLoc.Zone.ZoneCode}' is dedicated to another client.");
                    }
                }

                if (destLoc.StorageProfile?.DedicatedClientId is Guid profileClientId && profileClientId != lineClientId)
                {
                    throw new ValidationException(
                        nameof(line.StagingLocationId),
                        $"Receiving staging location '{destLoc.LocationCode}' is dedicated to another client.");
                }
            }

            var completingUserId = _currentUser.UserId ?? Guid.NewGuid();
            lockedSession.Complete(completingUserId);

            // Reconcile ASN quantities & status
            if (lockedSession.AdvanceShippingNotice != null)
            {
                foreach (var line in lockedSession.Lines)
                {
                    if (line.AsnLineItemId.HasValue)
                    {
                        lockedSession.AdvanceShippingNotice.RecordLineReceipt(line.AsnLineItemId.Value, line.QuantityReceived);
                    }
                    else
                    {
                        var matchingAsnLine = lockedSession.AdvanceShippingNotice.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
                        if (matchingAsnLine != null)
                        {
                            lockedSession.AdvanceShippingNotice.RecordLineReceipt(matchingAsnLine.Id, line.QuantityReceived);
                        }
                    }
                }

                lockedSession.AdvanceShippingNotice.CompleteReceiving();
            }

            // Reconcile PO line quantities & status
            if (lockedSession.PurchaseOrder != null)
            {
                foreach (var line in lockedSession.Lines)
                {
                    if (line.PurchaseOrderLineId.HasValue)
                    {
                        lockedSession.PurchaseOrder.RecordLineReceipt(line.PurchaseOrderLineId.Value, line.QuantityReceived);
                    }
                    else
                    {
                        var matchingPoLine = lockedSession.PurchaseOrder.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
                        if (matchingPoLine != null)
                        {
                            lockedSession.PurchaseOrder.RecordLineReceipt(matchingPoLine.Id, line.QuantityReceived);
                        }
                    }
                }
            }

            // Ledger Integration: Ingest into validated transient staging records
            var localLots = new Dictionary<(Guid ClientId, Guid ItemId, string LotNumber), InventoryLot>();
            var localStocks = new Dictionary<(Guid ClientId, Guid FacilityId, Guid ItemId, Guid LocationId, Guid? LotId), StockOnHand>();

            foreach (var line in lockedSession.Lines)
            {
                var clientId = lockedSession.ClientId ?? line.Item.ClientId;
                var stagingLocId = resolvedLineTargets[line.Id];

                // Persist the authoritative staging assignment onto the receipt line
                line.StagingLocationId = stagingLocId;

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
                var stockKey = (clientId, lockedSession.FacilityId, line.ItemId, stagingLocId, lotId);

                if (!localStocks.TryGetValue(stockKey, out var stock))
                {
                    stock = await _context.StockOnHand
                        .FirstOrDefaultAsync(s =>
                            s.ClientId == clientId &&
                            s.FacilityId == lockedSession.FacilityId &&
                            s.ItemId == line.ItemId &&
                            s.LocationId == stagingLocId &&
                            s.LotId == lotId,
                            cancellationToken);

                    if (stock == null)
                    {
                        stock = new StockOnHand
                        {
                            ClientId = clientId,
                            FacilityId = lockedSession.FacilityId,
                            ItemId = line.ItemId,
                            LocationId = stagingLocId,
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
                    FacilityId = lockedSession.FacilityId,
                    ItemId = line.ItemId,
                    LotId = lotId,
                    FromLocationId = null,
                    ToLocationId = stagingLocId,
                    TxType = InventoryTxType.Receive,
                    Quantity = line.QuantityReceived,
                    ReferenceType = "ReceivingSession",
                    ReferenceId = lockedSession.Id,
                    UserId = completingUserId,
                    TxTimestamp = DateTime.UtcNow,
                    Notes = $"Goods Received: Session {lockedSession.SessionNumber} (LPN: {line.Lpn}, Condition: {line.Condition})"
                };
                _context.InventoryTransactions.Add(ledgerTx);
            }

            // Commit all changes atomically at the end of the transaction
            await _context.SaveChangesAsync(cancellationToken);

            return MapToDto(lockedSession);
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
