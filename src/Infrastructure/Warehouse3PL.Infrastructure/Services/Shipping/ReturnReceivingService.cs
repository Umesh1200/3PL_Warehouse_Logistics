using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

public class ReturnReceivingService : IReturnReceivingService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ReturnReceivingService> _logger;

    public ReturnReceivingService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        ILogger<ReturnReceivingService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto> InitiateReturnReceivingAsync(
        InitiateReturnReceivingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Initiate return receiving request cannot be null.");
        }

        if (request.ReturnInboundHandoffId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ReturnInboundHandoffId), "Return inbound handoff ID is required.");
        }

        if (request.FacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.FacilityId), "Facility ID is required.");
        }

        if (request.OperatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.OperatorUserId), "Operator user ID is required.");
        }

        // 1. Retrieve Inbound Handoff
        var handoff = await _context.ReturnInboundHandoffs
            .Include(h => h.ReturnShipment)
            .Include(h => h.ReturnAuthorization)
            .Include(h => h.OriginalPackage)
                .ThenInclude(p => p.Lines)
            .Include(h => h.OriginalOrder)
                .ThenInclude(o => o.Lines)
            .FirstOrDefaultAsync(h => h.Id == request.ReturnInboundHandoffId, cancellationToken);

        if (handoff == null)
        {
            throw new EntityNotFoundException(nameof(ReturnInboundHandoff), request.ReturnInboundHandoffId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != handoff.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return receiving attempt.");
        }

        // 3. Facility Security & Authorization Check
        if (request.FacilityId != handoff.DestinationFacilityId)
        {
            throw new UnauthorizedException(
                $"Facility '{request.FacilityId}' is not authorized to receive return handoff assigned to destination facility '{handoff.DestinationFacilityId}'.");
        }

        if (_currentUserContext.FacilityId.HasValue &&
            !_currentUserContext.IsSuperAdmin &&
            _currentUserContext.FacilityId.Value != handoff.DestinationFacilityId)
        {
            throw new UnauthorizedException(
                $"User is not authorized for return receiving facility '{handoff.DestinationFacilityId}'.");
        }

        // 4. Handoff Status Invariants
        if (handoff.Status == ReturnInboundHandoffStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot initiate receiving for a cancelled inbound handoff.");
        }

        if (handoff.Status == ReturnInboundHandoffStatus.Received)
        {
            throw new InvalidDomainStateException("Inbound handoff has already been received and completed.");
        }

        // 5. Enforce Exactly One Active Receiving Session per Handoff
        var existingSession = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.ReturnInboundHandoffId == handoff.Id, cancellationToken);

        if (existingSession != null && existingSession.Status != ReturnReceivingSessionStatus.Cancelled)
        {
            throw new InvalidDomainStateException(
                $"A return receiving session ('{existingSession.SessionNumber}') is already active for this inbound handoff.");
        }

        // 6. Generate Session Number
        var sessionNumber = $"RRCV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // 7. Instantiate Aggregate Root
        var session = ReturnReceivingSession.Create(
            handoff.ClientId,
            request.FacilityId,
            handoff.Id,
            handoff.ReturnShipmentId,
            handoff.ReturnAuthorizationId,
            handoff.OriginalOrderId,
            handoff.OriginalPackageId,
            sessionNumber,
            request.OperatorUserId,
            request.Notes);

        // 8. Populate Expected Receiving Lines from Original Package or Order Lines
        var packageLines = await _context.PackageLines
            .Include(pl => pl.Item)
            .Where(pl => pl.PackageId == handoff.OriginalPackageId)
            .ToListAsync(cancellationToken);

        if (packageLines.Count > 0)
        {
            foreach (var pl in packageLines)
            {
                var line = new ReturnReceivingLine
                {
                    Id = Guid.NewGuid(),
                    ClientId = session.ClientId,
                    ReturnReceivingSessionId = session.Id,
                    ItemId = pl.ItemId,
                    OriginalOrderLineId = pl.OutboundOrderLineId,
                    Sku = pl.Item?.Sku ?? "UNKNOWN",
                    Description = pl.Item?.Description ?? string.Empty,
                    ExpectedQuantity = pl.Quantity,
                    ReceivedQuantity = 0m,
                    AcceptedQuantity = 0m,
                    DamagedQuantity = 0m,
                    QuarantinedQuantity = 0m,
                    IsInventoryPosted = false
                };
                session.Lines.Add(line);
            }
        }
        else
        {
            // Fallback to order lines if standalone package lines were not registered
            var orderLines = await _context.OutboundOrderLines
                .Include(ol => ol.Item)
                .Where(ol => ol.OutboundOrderId == handoff.OriginalOrderId)
                .ToListAsync(cancellationToken);

            foreach (var ol in orderLines)
            {
                var line = new ReturnReceivingLine
                {
                    Id = Guid.NewGuid(),
                    ClientId = session.ClientId,
                    ReturnReceivingSessionId = session.Id,
                    ItemId = ol.ItemId,
                    OriginalOrderLineId = ol.Id,
                    Sku = ol.Item?.Sku ?? "UNKNOWN",
                    Description = ol.Item?.Description ?? string.Empty,
                    ExpectedQuantity = ol.QuantityOrdered,
                    ReceivedQuantity = 0m,
                    AcceptedQuantity = 0m,
                    DamagedQuantity = 0m,
                    QuarantinedQuantity = 0m,
                    IsInventoryPosted = false
                };
                session.Lines.Add(line);
            }
        }

        // 9. Update Handoff Status to Processing
        handoff.MarkProcessing(request.DockLocation);

        _context.ReturnReceivingSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Return receiving session '{SessionNumber}' initiated for handoff '{HandoffRef}' at facility '{FacilityId}'.",
            session.SessionNumber, handoff.HandoffReference, request.FacilityId);

        return MapToDto(session);
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto> ConfirmPhysicalArrivalAsync(
        ConfirmPhysicalArrivalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Confirm physical arrival request cannot be null.");
        }

        if (request.SessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SessionId), "Session ID is required.");
        }

        if (request.OperatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.OperatorUserId), "Operator user ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ScannedBarcode))
        {
            throw new ValidationException(nameof(request.ScannedBarcode), "Scanned barcode is required.");
        }

        // 1. Retrieve Session with Handoff and Package
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.ReturnInboundHandoff)
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReturnReceivingSession), request.SessionId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != session.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return receiving confirmation attempt.");
        }

        // 3. Prevent Duplicate Scans & Invalid State Transitions
        if (session.Status != ReturnReceivingSessionStatus.Started)
        {
            if (session.Status == ReturnReceivingSessionStatus.PhysicalReceiptConfirmed ||
                session.Status == ReturnReceivingSessionStatus.Inspected ||
                session.Status == ReturnReceivingSessionStatus.Completed)
            {
                throw new InvalidDomainStateException(
                    "Physical package arrival has already been confirmed for this session. Duplicate scan prevented.");
            }

            throw new InvalidDomainStateException($"Cannot confirm physical arrival in '{session.Status}' status.");
        }

        // 4. Validate Scanned Barcode against Package/Handoff credentials
        var scanned = request.ScannedBarcode.Trim();
        var handoff = session.ReturnInboundHandoff;

        var isMatch = string.Equals(scanned, handoff.TrackingNumber, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(scanned, handoff.ReturnRmaNumber, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(scanned, handoff.HandoffReference, StringComparison.OrdinalIgnoreCase);

        if (!isMatch)
        {
            var package = await _context.Packages
                .FirstOrDefaultAsync(p => p.Id == session.PackageId, cancellationToken);

            if (package != null && (string.Equals(scanned, package.PackageNumber, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(scanned, package.TrackingNumber, StringComparison.OrdinalIgnoreCase)))
            {
                isMatch = true;
            }
        }

        if (!isMatch)
        {
            throw new ValidationException(
                nameof(request.ScannedBarcode),
                $"Scanned barcode '{request.ScannedBarcode}' does not match expected return package credentials.");
        }

        // 5. Update Domain Aggregate State
        session.ConfirmPhysicalArrival(
            request.OperatorUserId,
            DateTime.UtcNow,
            request.PackageCondition,
            request.ConditionNotes);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Physical arrival confirmed for return receiving session '{SessionNumber}'. Condition: {Condition}.",
            session.SessionNumber, session.PackageCondition);

        return MapToDto(session);
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto> VerifyReceivedQuantitiesAsync(
        VerifyReceivedQuantitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Verify received quantities request cannot be null.");
        }

        if (request.SessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SessionId), "Session ID is required.");
        }

        // 1. Retrieve Session
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReturnReceivingSession), request.SessionId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != session.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return quantity verification attempt.");
        }

        // 3. State Validation
        if (session.Status == ReturnReceivingSessionStatus.Started)
        {
            throw new InvalidDomainStateException("Physical arrival must be confirmed prior to quantity verification.");
        }

        if (session.Status == ReturnReceivingSessionStatus.Completed ||
            session.Status == ReturnReceivingSessionStatus.Cancelled ||
            session.IsInventoryPosted)
        {
            throw new InvalidDomainStateException(
                $"Cannot verify quantities for session '{session.SessionNumber}' in status '{session.Status}'.");
        }

        // 4. Update Verified Lines and Record Discrepancies
        if (request.Lines != null && request.Lines.Count > 0)
        {
            foreach (var reqLine in request.Lines)
            {
                var line = session.Lines.FirstOrDefault(l => l.Id == reqLine.LineId);
                if (line == null)
                {
                    throw new ValidationException(
                        nameof(reqLine.LineId),
                        $"Return receiving line '{reqLine.LineId}' not found in session.");
                }

                line.RecordReceivedQuantity(reqLine.ReceivedQuantity);

                // Detect shortages or overages
                if (line.ReceivedQuantity < line.ExpectedQuantity)
                {
                    var shortage = ReturnReceivingDiscrepancy.Create(
                        session.ClientId,
                        session.Id,
                        line.ItemId,
                        line.Sku,
                        line.Sku,
                        ReturnDiscrepancyType.Shortage,
                        line.ExpectedQuantity,
                        line.ReceivedQuantity,
                        $"Quantity shortage: Expected {line.ExpectedQuantity}, physically received {line.ReceivedQuantity}.");

                    session.Discrepancies.Add(shortage);
                }
                else if (line.ReceivedQuantity > line.ExpectedQuantity)
                {
                    var excess = ReturnReceivingDiscrepancy.Create(
                        session.ClientId,
                        session.Id,
                        line.ItemId,
                        line.Sku,
                        line.Sku,
                        ReturnDiscrepancyType.Excess,
                        line.ExpectedQuantity,
                        line.ReceivedQuantity,
                        $"Quantity excess: Expected {line.ExpectedQuantity}, physically received {line.ReceivedQuantity}.");

                    session.Discrepancies.Add(excess);
                }
            }
        }

        // 5. Handle Unannounced / Unexpected Return Items
        if (request.UnannouncedItems != null && request.UnannouncedItems.Count > 0)
        {
            foreach (var unannounced in request.UnannouncedItems)
            {
                var catalogItem = await _context.Items
                    .FirstOrDefaultAsync(i => i.ClientId == session.ClientId && i.Sku == unannounced.Sku.Trim(), cancellationToken);

                var disc = ReturnReceivingDiscrepancy.Create(
                    session.ClientId,
                    session.Id,
                    catalogItem?.Id,
                    null,
                    unannounced.Sku.Trim(),
                    ReturnDiscrepancyType.UnannouncedReturn,
                    0m,
                    unannounced.ReceivedQuantity,
                    unannounced.Notes ?? $"Unannounced returned item SKU '{unannounced.Sku}'.");

                session.Discrepancies.Add(disc);

                if (catalogItem != null)
                {
                    var newLine = new ReturnReceivingLine
                    {
                        Id = Guid.NewGuid(),
                        ClientId = session.ClientId,
                        ReturnReceivingSessionId = session.Id,
                        ItemId = catalogItem.Id,
                        Sku = catalogItem.Sku,
                        Description = catalogItem.Description,
                        ExpectedQuantity = 0m,
                        ReceivedQuantity = unannounced.ReceivedQuantity,
                        AcceptedQuantity = 0m,
                        DamagedQuantity = 0m,
                        QuarantinedQuantity = 0m,
                        IsInventoryPosted = false
                    };
                    session.Lines.Add(newLine);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Quantity verification completed for session '{SessionNumber}'. Discrepancies logged: {Count}.",
            session.SessionNumber, session.Discrepancies.Count);

        return MapToDto(session);
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto> InspectAndAssignDispositionAsync(
        InspectAndAssignDispositionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Inspect and assign disposition request cannot be null.");
        }

        if (request.SessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SessionId), "Session ID is required.");
        }

        if (request.Dispositions == null || request.Dispositions.Count == 0)
        {
            throw new ValidationException(nameof(request.Dispositions), "At least one line disposition is required.");
        }

        // 1. Retrieve Session
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReturnReceivingSession), request.SessionId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != session.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return inspection attempt.");
        }

        // 3. State Validation
        if (session.Status == ReturnReceivingSessionStatus.Started)
        {
            throw new InvalidDomainStateException("Physical arrival and quantity verification must be performed prior to inspection.");
        }

        if (session.Status == ReturnReceivingSessionStatus.Completed ||
            session.Status == ReturnReceivingSessionStatus.Cancelled ||
            session.IsInventoryPosted)
        {
            throw new InvalidDomainStateException($"Cannot inspect session in status '{session.Status}'.");
        }

        // 4. Assign Dispositions with Strict Quantity Conservation
        foreach (var disp in request.Dispositions)
        {
            var line = session.Lines.FirstOrDefault(l => l.Id == disp.LineId);
            if (line == null)
            {
                throw new ValidationException(nameof(disp.LineId), $"Return receiving line '{disp.LineId}' not found in session.");
            }

            var sum = disp.AcceptedQuantity + disp.DamagedQuantity + disp.QuarantinedQuantity;
            if (sum != line.ReceivedQuantity)
            {
                throw new ValidationException(
                    nameof(disp.AcceptedQuantity),
                    $"Sum of inspected quantities ({sum}) must strictly equal received quantity ({line.ReceivedQuantity}) for SKU '{line.Sku}'.");
            }

            line.AssignDisposition(
                disp.AcceptedQuantity,
                disp.DamagedQuantity,
                disp.QuarantinedQuantity,
                disp.Disposition,
                disp.TargetLocationId,
                disp.InspectionNotes,
                DateTime.UtcNow);
        }

        // 5. Mark Session Inspected if All Received Lines Have Dispositions
        var pendingLines = session.Lines.Where(l => l.ReceivedQuantity > 0 && !l.Disposition.HasValue).ToList();
        if (pendingLines.Count == 0)
        {
            session.MarkInspected(DateTime.UtcNow);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Inspection and disposition assigned for session '{SessionNumber}'. Status: {Status}.",
            session.SessionNumber, session.Status);

        return MapToDto(session);
    }

    /// <inheritdoc />
    public async Task<PostReturnInventoryResult> PostReturnInventoryAsync(
        PostReturnInventoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Post return inventory request cannot be null.");
        }

        if (request.SessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SessionId), "Session ID is required.");
        }

        if (request.OperatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.OperatorUserId), "Operator user ID is required.");
        }

        // 1. Retrieve Session
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.ReturnInboundHandoff)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReturnReceivingSession), request.SessionId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != session.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return inventory posting attempt.");
        }

        // 3. State & Duplicate Posting Safeguards
        if (session.Status != ReturnReceivingSessionStatus.Inspected)
        {
            throw new InvalidDomainStateException(
                $"Cannot post inventory for session in '{session.Status}' status. Quality inspection and disposition required.");
        }

        if (session.IsInventoryPosted)
        {
            throw new InvalidDomainStateException(
                "Inventory has already been posted for this return receiving session. Double posting prevented.");
        }

        // MDM21-F02-A & F03-C: Validate all destinations atomically inside a transaction under shared lifecycle locks
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            await Application.Features.WarehouseMaster.WarehouseInventoryDependencyGuard.EnsureFacilityIsActiveAsync(
                _context, session.FacilityId, cancellationToken);

            var linesToPost = session.Lines
                .Where(l => l.ReceivedQuantity > 0 && !l.IsInventoryPosted)
                .ToList();

            // 4. Resolve Safe Fallback Location ONLY from eligible active, unlocked, undamaged, client-compatible receiving/staging locations
            // (Never fall back to arbitrary active or structural aisle/rack locations)
            Guid? fallbackLocationId = request.DefaultLocationId;
            if (!fallbackLocationId.HasValue && linesToPost.Any(l => !l.TargetLocationId.HasValue))
            {
                var candidateReceiving = await _context.Locations
                    .AsNoTracking()
                    .Include(l => l.Zone)
                    .Include(l => l.StorageProfile)
                    .Where(l => l.FacilityId == session.FacilityId &&
                                !l.IsDeleted &&
                                l.IsActive &&
                                l.Status == LocationStatus.Active &&
                                !l.IsLocked &&
                                !l.IsDamaged &&
                                (l.IsReceiving || l.LocationType == LocationType.StagingLane) &&
                                l.LocationType != LocationType.Facility &&
                                l.LocationType != LocationType.Zone &&
                                l.LocationType != LocationType.Aisle &&
                                l.LocationType != LocationType.Rack &&
                                l.LocationType != LocationType.ShelfLevel &&
                                l.LocationType != LocationType.Mezzanine &&
                                l.LocationType != LocationType.DockDoor &&
                                l.LocationType != LocationType.PackingStation)
                    .OrderBy(l => l.LocationCode)
                    .ToListAsync(cancellationToken);

                var eligibleFallback = candidateReceiving.FirstOrDefault(l =>
                    (!l.Zone?.DedicatedClientId.HasValue ?? true || l.Zone!.DedicatedClientId!.Value == session.ClientId) &&
                    (!l.StorageProfile?.DedicatedClientId.HasValue ?? true || l.StorageProfile!.DedicatedClientId!.Value == session.ClientId));

                if (eligibleFallback != null)
                {
                    fallbackLocationId = eligibleFallback.Id;
                }
            }

            // Resolve every line's destination upfront and acquire deterministic sorted location lifecycle locks
            var resolvedTargets = new Dictionary<Guid, Guid>();
            foreach (var line in linesToPost)
            {
                var targetLocId = line.TargetLocationId ?? fallbackLocationId;
                if (!targetLocId.HasValue)
                {
                    throw new DomainException(
                        $"No eligible active receiving/staging or storage location found for returned SKU '{line.Sku}' at facility '{session.FacilityId}'.",
                        "RETURN_DESTINATION_UNAVAILABLE");
                }
                resolvedTargets[line.Id] = targetLocId.Value;
            }

            var distinctLocIds = resolvedTargets.Values.Distinct().OrderBy(id => id).ToList();
            var acquiredLocks = new List<IAsyncDisposable>(distinctLocIds.Count);
            try
            {
                foreach (var locId in distinctLocIds)
                {
                    acquiredLocks.Add(await _context.AcquirePutawayExecutionLocksAsync(null, locId, cancellationToken));
                }

                // Re-read all target locations after acquiring locks and validate EVERY line BEFORE posting any stock or ledger rows
                var lockedLocations = await _context.Locations
                    .AsNoTracking()
                    .Include(l => l.Zone)
                    .Include(l => l.StorageProfile)
                    .Where(l => distinctLocIds.Contains(l.Id) && !l.IsDeleted)
                    .ToDictionaryAsync(l => l.Id, cancellationToken);

                var lineItemIds = linesToPost.Select(l => l.ItemId).Distinct().ToList();
                var itemsById = await _context.Items
                    .AsNoTracking()
                    .Where(i => lineItemIds.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, cancellationToken);

                foreach (var line in linesToPost)
                {
                    var targetLocId = resolvedTargets[line.Id];
                    if (!lockedLocations.TryGetValue(targetLocId, out var destLoc) || destLoc.FacilityId != session.FacilityId)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Return destination location '{targetLocId}' does not exist in facility '{session.FacilityId}'.");
                    }

                    if (!destLoc.IsActive || destLoc.Status != LocationStatus.Active)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Return destination location '{destLoc.LocationCode}' is inactive or decommissioned (status: '{destLoc.Status}').");
                    }

                    if (destLoc.IsLocked || destLoc.IsDamaged)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Return destination location '{destLoc.LocationCode}' is locked or damaged and cannot accept returned inventory.");
                    }

                    var isEligibleType = destLoc.LocationType is LocationType.Bin
                        or LocationType.BulkFloor
                        or LocationType.Cantilever
                        or LocationType.StagingLane;
                    var isTransientStaging = destLoc.IsReceiving || destLoc.LocationType == LocationType.StagingLane;

                    if (!isEligibleType || (!destLoc.IsStorable && !isTransientStaging))
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Location '{destLoc.LocationCode}' (type '{destLoc.LocationType}', storable: {destLoc.IsStorable}) is not an eligible return staging or storage destination.");
                    }

                    if (line.Disposition == ReturnDisposition.RestockToAvailable &&
                        line.AcceptedQuantity > 0 &&
                        !destLoc.IsStorable &&
                        !isTransientStaging)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Restock-to-available disposition for SKU '{line.Sku}' requires a storable or receiving/staging destination.");
                    }

                    if (destLoc.Zone?.DedicatedClientId is Guid zoneClientId && zoneClientId != session.ClientId)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Return destination zone '{destLoc.Zone.ZoneCode}' is dedicated to another client.");
                    }

                    if (destLoc.StorageProfile?.DedicatedClientId is Guid profileClientId && profileClientId != session.ClientId)
                    {
                        throw new ValidationException(
                            nameof(request.DefaultLocationId),
                            $"Return destination location '{destLoc.LocationCode}' is dedicated to another client.");
                    }

                    if (!isTransientStaging)
                    {
                        if (itemsById.TryGetValue(line.ItemId, out var item))
                        {
                            await Application.Features.Putaway.PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(
                                _context, destLoc, item, line.ReceivedQuantity, cancellationToken);
                        }
                        else if (destLoc.StorageProfile != null && !destLoc.StorageProfile.IsPutawayAllowed)
                        {
                            throw new ValidationException(
                                nameof(request.DefaultLocationId),
                                $"Putaway is disallowed by storage profile on location '{destLoc.LocationCode}'.");
                        }
                    }
                }

                decimal totalAccepted = 0m;
                decimal totalDamaged = 0m;
                decimal totalQuarantined = 0m;
                var postedTxIds = new List<long>();
                int postedLineCount = 0;

                // 5. Post to Inventory Ledger & Update StockOnHand atomically
                foreach (var line in linesToPost)
                {
                    var targetLocId = resolvedTargets[line.Id];

                    // Find or initialize StockOnHand balance record
                    var stock = await _context.StockOnHand
                        .FirstOrDefaultAsync(s => s.ClientId == session.ClientId &&
                                                  s.FacilityId == session.FacilityId &&
                                                  s.ItemId == line.ItemId &&
                                                  s.LocationId == targetLocId &&
                                                  s.HandlingUnitId == null, cancellationToken);

                    if (stock == null)
                    {
                        stock = new StockOnHand
                        {
                            Id = Guid.NewGuid(),
                            ClientId = session.ClientId,
                            FacilityId = session.FacilityId,
                            ItemId = line.ItemId,
                            LocationId = targetLocId,
                            ReceivedAtUtc = DateTime.UtcNow,
                            Version = 1
                        };
                        _context.StockOnHand.Add(stock);
                    }

                    // Partition quantities into distinct inventory states
                    if (line.AcceptedQuantity > 0)
                    {
                        if (line.Disposition == ReturnDisposition.RestockToAvailable)
                        {
                            stock.QtyAvailable += line.AcceptedQuantity;
                        }
                        else if (line.Disposition == ReturnDisposition.RestockToRefurbished)
                        {
                            stock.QtyOnHold += line.AcceptedQuantity;
                        }
                        else
                        {
                            stock.QtyAvailable += line.AcceptedQuantity;
                        }

                        totalAccepted += line.AcceptedQuantity;
                    }

                    if (line.DamagedQuantity > 0)
                    {
                        stock.QtyDamaged += line.DamagedQuantity;
                        totalDamaged += line.DamagedQuantity;
                    }

                    if (line.QuarantinedQuantity > 0)
                    {
                        stock.QtyQuarantined += line.QuarantinedQuantity;
                        totalQuarantined += line.QuarantinedQuantity;
                    }

                    stock.Version++;

                    // Create Immutable Ledger Entry
                    var tx = new InventoryTransaction
                    {
                        ClientId = session.ClientId,
                        FacilityId = session.FacilityId,
                        ItemId = line.ItemId,
                        ToLocationId = targetLocId,
                        TxType = line.DamagedQuantity > 0 && line.AcceptedQuantity == 0 && line.QuarantinedQuantity == 0 && line.Disposition == ReturnDisposition.ScrapOrDestroy
                            ? InventoryTxType.Scrap
                            : InventoryTxType.Receive,
                        Quantity = line.ReceivedQuantity,
                        ReferenceType = "ReturnReceivingSession",
                        ReferenceId = session.Id,
                        UserId = request.OperatorUserId,
                        Notes = $"Return receipt: Sku '{line.Sku}', Session '{session.SessionNumber}', Accepted: {line.AcceptedQuantity}, Damaged: {line.DamagedQuantity}, Quarantined: {line.QuarantinedQuantity}, Disp: {line.Disposition}",
                        TxTimestamp = DateTime.UtcNow
                    };
                    _context.InventoryTransactions.Add(tx);

                    // Persist ledger entry inside transaction to acquire database identity
                    await _context.SaveChangesAsync(cancellationToken);

                    line.MarkInventoryPosted(tx.Id);
                    postedTxIds.Add(tx.Id);
                    postedLineCount++;
                }

                // Mark session inventory posted
                session.MarkInventoryPosted();
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Return inventory posted for session '{SessionNumber}'. Total items: {Lines}, Accepted: {Acc}, Damaged: {Dam}, Quarantined: {Quar}.",
                    session.SessionNumber, postedLineCount, totalAccepted, totalDamaged, totalQuarantined);

                return new PostReturnInventoryResult
                {
                    IsSuccess = true,
                    SessionId = session.Id,
                    TotalItemsPosted = postedLineCount,
                    TotalAcceptedQuantity = totalAccepted,
                    TotalDamagedQuantity = totalDamaged,
                    TotalQuarantinedQuantity = totalQuarantined,
                    InventoryTransactionIds = postedTxIds,
                    Message = "Return inventory successfully posted to warehouse stock and transaction ledger."
                };
            }
            finally
            {
                for (int i = acquiredLocks.Count - 1; i >= 0; i--)
                {
                    await acquiredLocks[i].DisposeAsync();
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompleteReturnReceivingResult> CompleteReturnReceivingAsync(
        CompleteReturnReceivingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException(nameof(request), "Complete return receiving request cannot be null.");
        }

        if (request.SessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.SessionId), "Session ID is required.");
        }

        if (request.OperatorUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.OperatorUserId), "Operator user ID is required.");
        }

        // 1. Retrieve Session with Aggregates
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.ReturnInboundHandoff)
            .Include(s => s.ReturnShipment)
            .Include(s => s.ReturnAuthorization)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(ReturnReceivingSession), request.SessionId);
        }

        // 2. Multi-Tenancy Validation
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != session.ClientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant return completion attempt.");
        }

        // 3. State & Inventory Invariant Checks
        if (session.Status == ReturnReceivingSessionStatus.Completed)
        {
            var originalOrder = await _context.OutboundOrders
                .FirstOrDefaultAsync(o => o.Id == session.OrderId, cancellationToken);

            return new CompleteReturnReceivingResult
            {
                IsSuccess = true,
                SessionId = session.Id,
                SessionStatus = session.Status,
                HandoffStatus = session.ReturnInboundHandoff.Status,
                ReturnShipmentStatus = session.ReturnShipment.Status,
                ReturnAuthorizationStatus = session.ReturnAuthorization.Status,
                OutboundOrderStatus = originalOrder?.Status ?? OutboundOrderStatus.Completed,
                Message = "Return receiving session has already been completed."
            };
        }

        if (session.Status != ReturnReceivingSessionStatus.Inspected)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete return receiving session in '{session.Status}' status. Inspection required.");
        }

        if (!session.IsInventoryPosted)
        {
            throw new InvalidDomainStateException(
                "Cannot complete return receiving session before inventory has been posted to warehouse ledger.");
        }

        // 4. Complete Session and Handoff
        session.Complete(DateTime.UtcNow);
        session.ReturnInboundHandoff.MarkReceived(request.Notes);

        // 5. Update ReturnShipment and ReturnAuthorization
        var primaryDisposition = session.Lines
            .Where(l => l.Disposition.HasValue)
            .OrderByDescending(l => l.AcceptedQuantity)
            .Select(l => l.Disposition!.Value)
            .FirstOrDefault();

        if (primaryDisposition == default)
        {
            primaryDisposition = ReturnDisposition.RestockToAvailable;
        }

        session.ReturnShipment.RecordReceivedAndInspected(
            request.OperatorUserId,
            primaryDisposition,
            DateTime.UtcNow,
            request.Notes);

        session.ReturnAuthorization.Complete(request.Notes);

        // 6. Enforce Outbound Order Status Invariance (Status must remain Completed)
        var order = await _context.OutboundOrders
            .FirstOrDefaultAsync(o => o.Id == session.OrderId, cancellationToken);

        if (order != null && order.Status != OutboundOrderStatus.Completed)
        {
            // Safeguard: strictly ensure order status is not mutated or reopened
            order.Status = OutboundOrderStatus.Completed;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Return receiving session '{SessionNumber}' completed successfully. Handoff: {HandoffStatus}, Shipment: {ShipmentStatus}, Auth: {AuthStatus}.",
            session.SessionNumber, session.ReturnInboundHandoff.Status, session.ReturnShipment.Status, session.ReturnAuthorization.Status);

        return new CompleteReturnReceivingResult
        {
            IsSuccess = true,
            SessionId = session.Id,
            SessionStatus = session.Status,
            HandoffStatus = session.ReturnInboundHandoff.Status,
            ReturnShipmentStatus = session.ReturnShipment.Status,
            ReturnAuthorizationStatus = session.ReturnAuthorization.Status,
            OutboundOrderStatus = order?.Status ?? OutboundOrderStatus.Completed,
            Message = "Return receiving session completed and all reverse logistics aggregates synchronized."
        };
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto?> GetSessionByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        return session == null ? null : MapToDto(session);
    }

    /// <inheritdoc />
    public async Task<ReturnReceivingSessionDto?> GetSessionByHandoffIdAsync(
        Guid handoffId,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ReturnReceivingSessions
            .Include(s => s.Lines)
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.ReturnInboundHandoffId == handoffId, cancellationToken);

        return session == null ? null : MapToDto(session);
    }

    private static ReturnReceivingSessionDto MapToDto(ReturnReceivingSession session)
    {
        return new ReturnReceivingSessionDto
        {
            Id = session.Id,
            ClientId = session.ClientId,
            FacilityId = session.FacilityId,
            ReturnInboundHandoffId = session.ReturnInboundHandoffId,
            ReturnShipmentId = session.ReturnShipmentId,
            ReturnAuthorizationId = session.ReturnAuthorizationId,
            OrderId = session.OrderId,
            PackageId = session.PackageId,
            SessionNumber = session.SessionNumber,
            Status = session.Status,
            ReceivedByUserId = session.ReceivedByUserId,
            StartedAtUtc = session.StartedAtUtc,
            PhysicalArrivalAtUtc = session.PhysicalArrivalAtUtc,
            InspectedAtUtc = session.InspectedAtUtc,
            CompletedAtUtc = session.CompletedAtUtc,
            PackageCondition = session.PackageCondition,
            PackageConditionNotes = session.PackageConditionNotes,
            IsInventoryPosted = session.IsInventoryPosted,
            Notes = session.Notes,
            Lines = session.Lines.Select(l => new ReturnReceivingLineDto
            {
                Id = l.Id,
                ItemId = l.ItemId,
                Sku = l.Sku,
                Description = l.Description,
                ExpectedQuantity = l.ExpectedQuantity,
                ReceivedQuantity = l.ReceivedQuantity,
                AcceptedQuantity = l.AcceptedQuantity,
                DamagedQuantity = l.DamagedQuantity,
                QuarantinedQuantity = l.QuarantinedQuantity,
                Disposition = l.Disposition,
                TargetLocationId = l.TargetLocationId,
                InspectionNotes = l.InspectionNotes,
                IsInventoryPosted = l.IsInventoryPosted,
                InventoryTransactionId = l.InventoryTransactionId
            }).ToList(),
            Discrepancies = session.Discrepancies.Select(d => new ReturnReceivingDiscrepancyDto
            {
                Id = d.Id,
                ItemId = d.ItemId,
                ExpectedSku = d.ExpectedSku,
                ActualSku = d.ActualSku,
                DiscrepancyType = d.DiscrepancyType,
                ExpectedQuantity = d.ExpectedQuantity,
                ReceivedQuantity = d.ReceivedQuantity,
                DiscrepancyQuantity = d.DiscrepancyQuantity,
                Notes = d.Notes,
                ReportedAtUtc = d.ReportedAtUtc
            }).ToList()
        };
    }
}
