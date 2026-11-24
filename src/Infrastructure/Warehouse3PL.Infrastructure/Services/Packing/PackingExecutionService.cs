using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Packing;

/// <summary>
/// Operational packing bench execution engine.
/// Coordinates workbench sessions, scan validations, atomic inventory transfers (Tote -> Carton),
/// scale gross/tare weight verification, and clean tote lifecycle release.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class PackingExecutionService : IPackingExecutionService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PackingExecutionService> _logger;

    public PackingExecutionService(
        IApplicationDbContext context,
        ILogger<PackingExecutionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PackingSessionResult> StartOrResumeSessionAsync(
        StartPackingSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Packing Station Location
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == request.PackingStationLocationId && !l.IsDeleted, cancellationToken);

        if (location == null)
        {
            throw new EntityNotFoundException(nameof(Location), request.PackingStationLocationId);
        }

        if (location.LocationType != LocationType.PackingStation)
        {
            throw new ValidationException(nameof(request.PackingStationLocationId),
                $"Location '{location.LocationCode}' is a '{location.LocationType}', not a 'PackingStation'.");
        }

        // 2. Validate Outbound Order
        var order = await _context.OutboundOrders
            .FirstOrDefaultAsync(o => o.Id == request.OutboundOrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            throw new EntityNotFoundException(nameof(OutboundOrder), request.OutboundOrderId);
        }

        if (order.Status == OutboundOrderStatus.Cancelled || order.Status == OutboundOrderStatus.Completed)
        {
            throw new InvalidDomainStateException(
                $"Cannot pack order '{order.OrderNumber}' with status '{order.Status}'.");
        }

        // 3. Concurrency Protection: Check for existing active/suspended session for this order
        var existingSession = await _context.PackingSessions
            .FirstOrDefaultAsync(s => s.OutboundOrderId == order.Id &&
                                     (s.Status == PackingSessionStatus.Active || s.Status == PackingSessionStatus.Suspended) &&
                                     !s.IsDeleted, cancellationToken);

        if (existingSession != null)
        {
            // If another operator has locked this order, reject concurrent session attempt
            if (existingSession.OperatorUserId != request.OperatorUserId)
            {
                throw new InvalidDomainStateException(
                    $"Order '{order.OrderNumber}' is already being packed in active session '{existingSession.Id}' by operator '{existingSession.OperatorUserId}'.");
            }

            // If same operator, resume session if suspended
            if (existingSession.Status == PackingSessionStatus.Suspended)
            {
                existingSession.Resume(request.OperatorUserId);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var activePkgs = await _context.Packages
                .CountAsync(p => p.OutboundOrderId == order.Id && p.Status == PackageStatus.Open && !p.IsDeleted, cancellationToken);

            return new PackingSessionResult
            {
                IsSuccess = true,
                SessionId = existingSession.Id,
                Status = existingSession.Status,
                OutboundOrderId = order.Id,
                OrderNumber = order.OrderNumber,
                PackingStationLocationId = existingSession.PackingStationLocationId,
                StationLocationCode = location.LocationCode,
                OperatorUserId = existingSession.OperatorUserId,
                StartedAtUtc = existingSession.StartedAtUtc,
                ActivePackagesCount = activePkgs,
                Message = "Resumed existing packing session."
            };
        }

        // 4. Create New Packing Session
        var session = new PackingSession
        {
            ClientId = order.ClientId,
            FacilityId = request.FacilityId,
            PackingStationLocationId = request.PackingStationLocationId,
            OutboundOrderId = order.Id,
            OperatorUserId = request.OperatorUserId,
            Status = PackingSessionStatus.Active,
            StartedAtUtc = DateTime.UtcNow
        };

        _context.PackingSessions.Add(session);

        if (order.Status != OutboundOrderStatus.InProgress)
        {
            order.Status = OutboundOrderStatus.InProgress;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created packing session '{SessionId}' for order '{OrderNumber}' at station '{Station}'",
            session.Id, order.OrderNumber, location.LocationCode);

        return new PackingSessionResult
        {
            IsSuccess = true,
            SessionId = session.Id,
            Status = session.Status,
            OutboundOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PackingStationLocationId = session.PackingStationLocationId,
            StationLocationCode = location.LocationCode,
            OperatorUserId = session.OperatorUserId,
            StartedAtUtc = session.StartedAtUtc,
            ActivePackagesCount = 0,
            Message = "Created new packing session."
        };
    }

    public async Task<CreatePackageResult> CreatePackageAsync(
        CreatePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.PackingSessions
            .Include(s => s.OutboundOrder)
            .FirstOrDefaultAsync(s => s.Id == request.PackingSessionId && !s.IsDeleted, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(PackingSession), request.PackingSessionId);
        }

        if (session.Status != PackingSessionStatus.Active)
        {
            throw new InvalidDomainStateException($"Cannot create package in session with status '{session.Status}'. Session must be Active.");
        }

        var order = session.OutboundOrder;
        var existingCount = await _context.Packages
            .CountAsync(p => p.OutboundOrderId == order.Id && !p.IsDeleted, cancellationToken);

        int sequence = existingCount + 1;
        string packageNumber = $"PKG-{order.OrderNumber}-{sequence:D2}";
        string cartonLpn = $"CTN-{order.OrderNumber}-{sequence:D2}";

        // 1. Create specialized HandlingUnit (1:1 with Package)
        var handlingUnit = new HandlingUnit
        {
            ClientId = order.ClientId,
            FacilityId = session.FacilityId,
            LpnCode = cartonLpn,
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse,
            CurrentLocationId = session.PackingStationLocationId,
            CurrentOutboundOrderId = order.Id
        };
        _context.HandlingUnits.Add(handlingUnit);

        // 2. Create Package aggregate
        var package = new Package
        {
            ClientId = order.ClientId,
            FacilityId = session.FacilityId,
            OutboundOrderId = order.Id,
            HandlingUnitId = handlingUnit.Id,
            HandlingUnit = handlingUnit,
            PackageNumber = packageNumber,
            CartonSequence = sequence,
            CurrentLocationId = session.PackingStationLocationId,
            Status = PackageStatus.Open,
            PackedByUserId = request.UserId
        };

        // 3. Apply Packaging Material if selected
        PackagingMaterial? material = null;
        if (request.PackagingMaterialId.HasValue)
        {
            material = await _context.PackagingMaterials
                .FirstOrDefaultAsync(m => m.Id == request.PackagingMaterialId.Value && !m.IsDeleted, cancellationToken);

            if (material == null)
            {
                throw new EntityNotFoundException(nameof(PackagingMaterial), request.PackagingMaterialId.Value);
            }

            // Verify tenant accessibility
            if (material.ClientId.HasValue && material.ClientId.Value != order.ClientId)
            {
                throw new ValidationException(nameof(request.PackagingMaterialId),
                    "Selected packaging material belongs to another client.");
            }

            package.ApplyPackagingMaterial(material);
        }

        _context.Packages.Add(package);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created package '{PackageNumber}' with HandlingUnit '{LPN}' for Order '{Order}'",
            package.PackageNumber, cartonLpn, order.OrderNumber);

        return new CreatePackageResult
        {
            IsSuccess = true,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            HandlingUnitId = handlingUnit.Id,
            HandlingUnitLpn = cartonLpn,
            Status = package.Status,
            PackagingMaterialId = material?.Id,
            PackagingMaterialCode = material?.Code,
            LengthCm = package.LengthCm,
            WidthCm = package.WidthCm,
            HeightCm = package.HeightCm,
            TareWeightKg = package.TareWeightKg,
            Message = "Carton created successfully."
        };
    }

    public async Task<PackItemResult> PackItemAsync(
        PackItemRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecuteWithPackageLockAsync(request.PackageId, shipmentId: null, async () =>
        {
            // 1. Session and Order Validation
            var session = await _context.PackingSessions
                .Include(s => s.OutboundOrder)
                    .ThenInclude(o => o.Lines)
                        .ThenInclude(l => l.Item)
                            .ThenInclude(i => i.Barcodes)
                .FirstOrDefaultAsync(s => s.Id == request.PackingSessionId && !s.IsDeleted, cancellationToken);

            if (session == null)
            {
                throw new EntityNotFoundException(nameof(PackingSession), request.PackingSessionId);
            }

            if (session.Status != PackingSessionStatus.Active)
            {
                throw new InvalidDomainStateException($"Cannot pack items in session with status '{session.Status}'. Session must be Active.");
            }

            var order = session.OutboundOrder;

            // 2. Package Validation
            var package = await _context.Packages
                .Include(p => p.HandlingUnit)
                .Include(p => p.PackagingMaterial)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

            if (package == null)
            {
                throw new EntityNotFoundException(nameof(Package), request.PackageId);
            }

            if (package.OutboundOrderId != order.Id)
            {
                throw new ValidationException(nameof(request.PackageId), "Package does not belong to the active packing session order.");
            }

            if (package.Status != PackageStatus.Open)
            {
                throw new InvalidDomainStateException($"Cannot pack items into package '{package.PackageNumber}' with status '{package.Status}'. Package must be Open.");
            }

            // 2b. Idempotency Retry & Payload Fingerprint Verification
            var normalizedIdempKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : request.IdempotencyKey.Trim();
            var idempMarker = normalizedIdempKey != null ? $"[IDEMP:{normalizedIdempKey}]" : null;
            var fingerprint = normalizedIdempKey != null ? ComputePackFingerprint(request) : null;
            var fpMarker = fingerprint != null ? $"[FP:{fingerprint}]" : null;

            if (idempMarker != null)
            {
                var existingPackTx = await _context.InventoryTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.FacilityId == session.FacilityId &&
                                              t.TxType == InventoryTxType.Pack &&
                                              t.Notes != null &&
                                              t.Notes.Contains(idempMarker), cancellationToken);

                if (existingPackTx != null)
                {
                    if (fpMarker == null || existingPackTx.Notes == null ||
                        !existingPackTx.Notes.Contains(fpMarker, StringComparison.Ordinal))
                    {
                        throw new ConcurrencyException(
                            $"Idempotency key '{normalizedIdempKey}' was already used with a different packing payload.",
                            "IDEMPOTENCY_CONFLICT");
                    }

                    var existingItem = await _context.Items
                        .AsNoTracking()
                        .Where(i => i.Id == existingPackTx.ItemId)
                        .Select(i => new { i.Sku })
                        .FirstOrDefaultAsync(cancellationToken);

                    var existingLine = package.Lines.FirstOrDefault(l =>
                        l.ItemId == existingPackTx.ItemId &&
                        l.LotId == existingPackTx.LotId);

                    var orderLine = order.Lines.FirstOrDefault(l =>
                        !l.IsDeleted &&
                        l.Id == existingLine?.OutboundOrderLineId);

                    decimal packedForOrderLine = orderLine == null
                        ? 0m
                        : await _context.PackageLines
                            .Where(pl => pl.OutboundOrderLineId == orderLine.Id && !pl.Package.IsDeleted)
                            .SumAsync(pl => pl.Quantity, cancellationToken);

                    decimal remainingToteQty = existingLine?.SourceHandlingUnitId == null
                        ? 0m
                        : await _context.StockOnHand
                            .AsNoTracking()
                            .Where(s => s.HandlingUnitId == existingLine.SourceHandlingUnitId.Value &&
                                        s.ItemId == existingPackTx.ItemId &&
                                        s.LotId == existingPackTx.LotId)
                            .SumAsync(s => s.QtyAllocated, cancellationToken);

                    decimal allAllocated = order.Lines.Where(l => !l.IsDeleted).Sum(l => l.QuantityAllocated);
                    decimal allPacked = await _context.PackageLines
                        .Where(pl => pl.Package.OutboundOrderId == order.Id && !pl.Package.IsDeleted)
                        .SumAsync(pl => pl.Quantity, cancellationToken);

                    return new PackItemResult
                    {
                        IsSuccess = true,
                        PackageId = package.Id,
                        PackageNumber = package.PackageNumber,
                        PackageLineId = existingLine?.Id ?? Guid.Empty,
                        ItemId = existingPackTx.ItemId,
                        Sku = existingItem?.Sku ?? request.ScannedItemBarcode,
                        LotNumber = request.ScannedLotNumber,
                        QuantityPacked = existingPackTx.Quantity,
                        RemainingToteQuantity = remainingToteQty,
                        RemainingOrderLineDemand = orderLine == null ? 0m : Math.Max(0m, orderLine.QuantityAllocated - packedForOrderLine),
                        SourceToteLpn = request.ScannedToteLpn.Trim(),
                        CartonLpn = package.HandlingUnit.LpnCode,
                        InventoryTransactionId = existingPackTx.Id,
                        IsOrderLineComplete = orderLine != null && packedForOrderLine >= orderLine.QuantityAllocated,
                        IsOrderFullyPacked = allAllocated > 0 && allPacked >= allAllocated,
                        Message = "Item already packed (idempotent retry)."
                    };
                }
            }

            // 3. Tote Scan Validation
            if (string.IsNullOrWhiteSpace(request.ScannedToteLpn))
            {
                throw new ValidationException(nameof(request.ScannedToteLpn), "Source tote LPN is required.");
            }

            var scannedToteLpn = request.ScannedToteLpn.Trim();
            var tote = await _context.HandlingUnits
                .FirstOrDefaultAsync(h => h.FacilityId == session.FacilityId &&
                                          h.LpnCode == scannedToteLpn &&
                                          !h.IsDeleted, cancellationToken);

            if (tote == null)
            {
                throw new ValidationException(nameof(request.ScannedToteLpn),
                    $"Source tote with LPN '{scannedToteLpn}' was not found in facility.");
            }

            // Cross-tenant protection: tote must match order client
            if (tote.ClientId != order.ClientId)
            {
                throw new ValidationException(nameof(request.ScannedToteLpn),
                    $"Tote '{scannedToteLpn}' belongs to a different client. Cross-tenant packing rejected.");
            }

            if (tote.Status != HandlingUnitStatus.InUse && tote.Status != HandlingUnitStatus.InTransit)
            {
                throw new InvalidDomainStateException(
                    $"Tote '{scannedToteLpn}' has status '{tote.Status}' and cannot be used for packing.");
            }

            // 4. Item Scan Validation
            if (string.IsNullOrWhiteSpace(request.ScannedItemBarcode))
            {
                throw new ValidationException(nameof(request.ScannedItemBarcode), "Item barcode is required.");
            }

            var scannedBarcode = request.ScannedItemBarcode.Trim();
            var matchingLine = order.Lines.FirstOrDefault(l =>
                !l.IsDeleted &&
                (string.Equals(l.Item.Sku, scannedBarcode, StringComparison.OrdinalIgnoreCase) ||
                 l.Item.Barcodes.Any(b => string.Equals(b.BarcodeValue, scannedBarcode, StringComparison.OrdinalIgnoreCase))));

            if (matchingLine == null)
            {
                throw new ValidationException(nameof(request.ScannedItemBarcode),
                    $"Scanned item barcode '{scannedBarcode}' does not match any item on Order '{order.OrderNumber}'.");
            }

            var item = matchingLine.Item;

            // 5. Lot Validation
            if (item.IsLotTracked && !string.IsNullOrWhiteSpace(request.ScannedLotNumber))
            {
                var scannedLot = request.ScannedLotNumber.Trim();
                if (!string.IsNullOrWhiteSpace(matchingLine.RequiredLotNumber) &&
                    !string.Equals(matchingLine.RequiredLotNumber.Trim(), scannedLot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(nameof(request.ScannedLotNumber),
                        $"Scanned lot '{scannedLot}' does not match order requirement '{matchingLine.RequiredLotNumber}'.");
                }
            }

            // 6. Quantity Validation
            if (request.Quantity <= 0)
            {
                throw new ValidationException(nameof(request.Quantity), "Pack quantity must be greater than zero.");
            }

            // Locate StockOnHand in Tote
            var toteStockList = await _context.StockOnHand
                .Include(s => s.Lot)
                .Where(s => s.HandlingUnitId == tote.Id &&
                            s.ItemId == item.Id &&
                            s.QtyAllocated > 0)
                .ToListAsync(cancellationToken);

            if (toteStockList.Count == 0)
            {
                throw new ValidationException(nameof(request.ScannedToteLpn),
                    $"Tote '{scannedToteLpn}' does not contain picked stock for SKU '{item.Sku}'.");
            }

            StockOnHand? toteStock = null;
            if (!string.IsNullOrWhiteSpace(request.ScannedLotNumber))
            {
                var scannedLot = request.ScannedLotNumber.Trim();
                toteStock = toteStockList.FirstOrDefault(s => s.Lot != null &&
                    string.Equals(s.Lot.LotNumber.Trim(), scannedLot, StringComparison.OrdinalIgnoreCase));

                if (toteStock == null)
                {
                    throw new ValidationException(nameof(request.ScannedLotNumber),
                        $"Tote '{scannedToteLpn}' contains SKU '{item.Sku}', but not lot '{scannedLot}'.");
                }
            }
            else
            {
                toteStock = toteStockList.First();
            }

            if (request.Quantity > toteStock.QtyAllocated)
            {
                throw new ValidationException(nameof(request.Quantity),
                    $"Pack quantity {request.Quantity} exceeds available picked quantity {toteStock.QtyAllocated} in tote '{tote.LpnCode}'.");
            }

            // Check already packed quantity for order line across all packages
            var packedForLine = await _context.PackageLines
                .Where(pl => pl.OutboundOrderLineId == matchingLine.Id && !pl.Package.IsDeleted)
                .SumAsync(pl => pl.Quantity, cancellationToken);

            var remainingLineDemand = matchingLine.QuantityAllocated - packedForLine;
            if (request.Quantity > remainingLineDemand)
            {
                throw new ValidationException(nameof(request.Quantity),
                    $"Pack quantity {request.Quantity} exceeds remaining order line un-packed demand {remainingLineDemand}.");
            }

            // Locate Allocation link
            var allocation = await _context.InventoryAllocations
                .FirstOrDefaultAsync(a => a.OutboundOrderLineId == matchingLine.Id &&
                                          a.ItemId == item.Id &&
                                          (a.TargetHandlingUnitId == tote.Id || a.TargetHandlingUnitId == null) &&
                                          !a.IsDeleted, cancellationToken);

            if (allocation == null)
            {
                throw new DomainException($"No active allocation found for order line '{matchingLine.Id}'.");
            }

            // Check box max weight limit using EACH packed line's actual item weight
            if (package.PackagingMaterial != null && package.PackagingMaterial.MaxWeightKg > 0)
            {
                var currentNetWeight = package.Lines.Sum(l => l.Quantity * ResolvePackedLineUnitWeightKg(l, order));
                var incomingUnitWeight = ResolveItemUnitWeightKg(item);
                var newTotalNetWeight = currentNetWeight + (request.Quantity * incomingUnitWeight);
                if (newTotalNetWeight > package.PackagingMaterial.MaxWeightKg)
                {
                    throw new ValidationException(nameof(request.Quantity),
                        $"Adding {request.Quantity} units would cause package net weight ({newTotalNetWeight:F2}kg) to exceed carton limit ({package.PackagingMaterial.MaxWeightKg:F2}kg).");
                }
            }

            // 7. Atomic Inventory Transfer (Physical Conservation: Delta Q_tote = -Q, Delta Q_carton = +Q)
            // a. Deduct from tote
            toteStock.QtyAllocated -= request.Quantity;
            toteStock.Version++;

            // b. Add to carton
            var cartonStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s => s.HandlingUnitId == package.HandlingUnitId &&
                                          s.ItemId == item.Id &&
                                          s.LotId == toteStock.LotId, cancellationToken);

            if (cartonStock != null)
            {
                cartonStock.QtyAllocated += request.Quantity;
                cartonStock.Version++;
            }
            else
            {
                cartonStock = new StockOnHand
                {
                    Id = Guid.NewGuid(),
                    ClientId = order.ClientId,
                    FacilityId = session.FacilityId,
                    ItemId = item.Id,
                    LotId = toteStock.LotId,
                    LocationId = session.PackingStationLocationId,
                    HandlingUnitId = package.HandlingUnitId,
                    LpnCode = package.HandlingUnit.LpnCode,
                    QtyAvailable = 0m, // Invariant: QtyAvailable must remain 0
                    QtyAllocated = request.Quantity,
                    ReceivedAtUtc = DateTime.UtcNow,
                    Version = 1
                };
                _context.StockOnHand.Add(cartonStock);
            }

            // c. Record Package Line
            package.AddLine(matchingLine.Id, item.Id, toteStock.LotId, request.Quantity, tote.Id, allocation.Id);

            // d. Record Immutable Inventory Transaction Ledger
            var tx = new InventoryTransaction
            {
                ClientId = order.ClientId,
                FacilityId = session.FacilityId,
                ItemId = item.Id,
                LotId = toteStock.LotId,
                FromLocationId = tote.CurrentLocationId ?? session.PackingStationLocationId,
                ToLocationId = session.PackingStationLocationId,
                TxType = InventoryTxType.Pack,
                Quantity = request.Quantity,
                ReferenceType = "Package",
                ReferenceId = package.Id,
                UserId = request.UserId,
                Notes = idempMarker != null
                    ? $"Packed {request.Quantity} units of '{item.Sku}' from Tote '{tote.LpnCode}' into Package '{package.PackageNumber}' {idempMarker}{fpMarker}"
                    : $"Packed {request.Quantity} units of '{item.Sku}' from Tote '{tote.LpnCode}' into Package '{package.PackageNumber}'",
                TxTimestamp = DateTime.UtcNow
            };
            _context.InventoryTransactions.Add(tx);

            await _context.SaveChangesAsync(cancellationToken);

            // Calculate order packing completion
            var updatedPackedForLine = packedForLine + request.Quantity;
            var isLineComplete = updatedPackedForLine >= matchingLine.QuantityAllocated;

            var allLinesAllocated = order.Lines.Sum(l => l.QuantityAllocated);
            var totalOrderPacked = await _context.PackageLines
                .Where(pl => pl.Package.OutboundOrderId == order.Id && !pl.Package.IsDeleted)
                .SumAsync(pl => pl.Quantity, cancellationToken);

            var isOrderFullyPacked = totalOrderPacked >= allLinesAllocated && allLinesAllocated > 0;

            _logger.LogInformation(
                "Packed {Qty} units of SKU '{Sku}' into Package '{Package}' from Tote '{Tote}'. Remaining in tote: {RemainingTote}",
                request.Quantity, item.Sku, package.PackageNumber, tote.LpnCode, toteStock.QtyAllocated);

            var pkgLine = package.Lines.First(l => l.OutboundOrderLineId == matchingLine.Id && l.LotId == toteStock.LotId);

            return new PackItemResult
            {
                IsSuccess = true,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                PackageLineId = pkgLine.Id,
                ItemId = item.Id,
                Sku = item.Sku,
                LotNumber = toteStock.Lot?.LotNumber,
                QuantityPacked = request.Quantity,
                RemainingToteQuantity = toteStock.QtyAllocated,
                RemainingOrderLineDemand = Math.Max(0m, matchingLine.QuantityAllocated - updatedPackedForLine),
                SourceToteLpn = tote.LpnCode,
                CartonLpn = package.HandlingUnit.LpnCode,
                InventoryTransactionId = tx.Id,
                IsOrderLineComplete = isLineComplete,
                IsOrderFullyPacked = isOrderFullyPacked,
                Message = $"Successfully packed {request.Quantity} units."
            };
        }, cancellationToken);
    }

    private static decimal ResolveItemUnitWeightKg(Item? item)
        => item != null && item.WeightKg > 0m ? item.WeightKg : 0.5m;

    private static decimal ResolvePackedLineUnitWeightKg(PackageLine line, OutboundOrder order)
    {
        var resolvedItem = line.Item ?? order.Lines.FirstOrDefault(ol => ol.ItemId == line.ItemId)?.Item;
        return ResolveItemUnitWeightKg(resolvedItem);
    }

    private static string ComputePackFingerprint(PackItemRequest request)
    {
        var canonical = string.Join("|",
            request.PackingSessionId.ToString("D"),
            request.PackageId.ToString("D"),
            (request.ScannedToteLpn ?? string.Empty).Trim().ToUpperInvariant(),
            (request.ScannedItemBarcode ?? string.Empty).Trim().ToUpperInvariant(),
            (request.ScannedLotNumber ?? string.Empty).Trim().ToUpperInvariant(),
            request.Quantity.ToString("0.########", CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..24];
    }

    public async Task<ClosePackageResult> ClosePackageAsync(
        ClosePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .Include(p => p.Lines)
                .ThenInclude(l => l.Item)
            .Include(p => p.PackagingMaterial)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        if (package.PackagingMaterial == null && package.PackagingMaterialId.HasValue)
        {
            package.PackagingMaterial = await _context.PackagingMaterials
                .FirstOrDefaultAsync(m => m.Id == package.PackagingMaterialId.Value && !m.IsDeleted, cancellationToken);
        }

        package.Close(request.ScaleGrossWeightKg, request.UserId);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Package '{PackageNumber}' sealed. Scale Gross: {Gross}kg, Tare: {Tare}kg, Net: {Net}kg",
            package.PackageNumber, package.GrossWeightKg, package.TareWeightKg, package.NetWeightKg);

        return new ClosePackageResult
        {
            IsSuccess = true,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            GrossWeightKg = package.GrossWeightKg,
            TareWeightKg = package.TareWeightKg,
            NetWeightKg = package.NetWeightKg,
            VolumetricWeightKg = package.VolumetricWeightKg,
            Status = package.Status,
            Message = "Package sealed successfully."
        };
    }

    public async Task<ReleaseToteResult> ReleaseToteAsync(
        ReleaseToteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToteLpn))
        {
            throw new ValidationException(nameof(request.ToteLpn), "Tote LPN is required.");
        }

        var lpn = request.ToteLpn.Trim();
        var tote = await _context.HandlingUnits
            .FirstOrDefaultAsync(h => h.FacilityId == request.FacilityId &&
                                      h.LpnCode == lpn &&
                                      !h.IsDeleted, cancellationToken);

        if (tote == null)
        {
            throw new EntityNotFoundException(nameof(HandlingUnit), lpn);
        }

        // Rule 1: No remaining inventory associated with tote
        var remainingStock = await _context.StockOnHand
            .Where(s => s.HandlingUnitId == tote.Id && (s.QtyAllocated > 0 || s.QtyAvailable > 0))
            .SumAsync(s => s.QtyAllocated + s.QtyAvailable, cancellationToken);

        if (remainingStock > 0)
        {
            throw new InvalidDomainStateException(
                $"Cannot release tote '{tote.LpnCode}'. It still holds {remainingStock} units of active inventory.");
        }

        // Rule 2: No active pick tasks using this tote
        var hasActiveTasks = await _context.PickTasks
            .AnyAsync(t => t.TargetHandlingUnitId == tote.Id &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.Cancelled &&
                           t.Status != PickTaskStatus.ShortPicked, cancellationToken);

        if (hasActiveTasks)
        {
            throw new InvalidDomainStateException(
                $"Cannot release tote '{tote.LpnCode}'. Active pick tasks are still assigned to this handling unit.");
        }

        tote.Release();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Tote '{LpnCode}' released back to Available pool", tote.LpnCode);

        return new ReleaseToteResult
        {
            IsSuccess = true,
            HandlingUnitId = tote.Id,
            LpnCode = tote.LpnCode,
            Status = tote.Status,
            Message = $"Tote '{tote.LpnCode}' returned to Available pool."
        };
    }

    public async Task<CompletePackingSessionResult> CompletePackingSessionAsync(
        CompletePackingSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.PackingSessions
            .Include(s => s.OutboundOrder)
                .ThenInclude(o => o.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.PackingSessionId && !s.IsDeleted, cancellationToken);

        if (session == null)
        {
            throw new EntityNotFoundException(nameof(PackingSession), request.PackingSessionId);
        }

        // Check all packages for this session are packed
        var openPackages = await _context.Packages
            .Where(p => p.OutboundOrderId == session.OutboundOrderId &&
                        p.Status == PackageStatus.Open &&
                        !p.IsDeleted)
            .ToListAsync(cancellationToken);

        if (openPackages.Count > 0)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete packing session. There are {openPackages.Count} open packages awaiting scale weight and sealing.");
        }

        // Check all allocated order lines have been packed
        var order = session.OutboundOrder;
        var totalAllocated = order.Lines.Sum(l => l.QuantityAllocated);
        var totalPacked = await _context.PackageLines
            .Where(pl => pl.Package.OutboundOrderId == order.Id && !pl.Package.IsDeleted)
            .SumAsync(pl => pl.Quantity, cancellationToken);

        if (totalPacked < totalAllocated)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete packing session. Order '{order.OrderNumber}' is only partially packed ({totalPacked}/{totalAllocated} units).");
        }

        session.Complete(request.UserId);
        await _context.SaveChangesAsync(cancellationToken);

        var packages = await _context.Packages
            .Where(p => p.OutboundOrderId == order.Id && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Completed packing session '{SessionId}' for Order '{OrderNumber}'", session.Id, order.OrderNumber);

        return new CompletePackingSessionResult
        {
            IsSuccess = true,
            SessionId = session.Id,
            Status = session.Status,
            CompletedAtUtc = session.CompletedAtUtc,
            TotalPackagesCount = packages.Count,
            TotalWeightKg = packages.Sum(p => p.GrossWeightKg),
            Message = "Packing session completed successfully."
        };
    }
}
