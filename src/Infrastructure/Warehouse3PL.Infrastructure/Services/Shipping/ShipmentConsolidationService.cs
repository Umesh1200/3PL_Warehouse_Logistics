using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Operational shipment consolidation and dock dispatch engine.
/// Coordinates multi-carton freight grouping, dock staging scans, trailer loading verification,
/// immutable Bill of Lading generation, and authorized driver custody handover.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation (Step 3B).
/// </summary>
public class ShipmentConsolidationService : IShipmentConsolidationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ShipmentConsolidationService> _logger;

    public ShipmentConsolidationService(
        IApplicationDbContext context,
        ILogger<ShipmentConsolidationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            throw new ValidationException(nameof(request.CarrierCode), "Carrier code is required.");
        }

        var client = await _context.Clients.FindAsync([request.ClientId], cancellationToken);
        if (client == null) throw new EntityNotFoundException(nameof(client), request.ClientId);

        var facility = await _context.Facilities.FindAsync([request.FacilityId], cancellationToken);
        if (facility == null) throw new EntityNotFoundException(nameof(facility), request.FacilityId);

        if (request.StagingLocationId.HasValue)
        {
            var stagingLoc = await _context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.StagingLocationId.Value && !l.IsDeleted, cancellationToken);
            if (stagingLoc == null)
            {
                throw new EntityNotFoundException(nameof(Location), request.StagingLocationId.Value);
            }
            if (stagingLoc.FacilityId != request.FacilityId)
            {
                throw new ValidationException(nameof(request.StagingLocationId),
                    $"Staging location '{stagingLoc.LocationCode}' belongs to a different facility.");
            }
            if (stagingLoc.LocationType != LocationType.StagingLane && stagingLoc.LocationType != LocationType.DockDoor)
            {
                throw new ValidationException(nameof(request.StagingLocationId),
                    $"Location '{stagingLoc.LocationCode}' is '{stagingLoc.LocationType}'. Shipment staging requires StagingLane or DockDoor.");
            }
        }

        var count = await _context.Shipments
            .CountAsync(s => s.FacilityId == request.FacilityId, cancellationToken);

        string shipmentNumber = $"SHP-{request.CarrierCode.ToUpperInvariant().Replace("SIM-", "")}-{DateTime.UtcNow:yyyyMMdd}-{count + 1:D3}";

        var shipment = new Shipment
        {
            ClientId = request.ClientId,
            FacilityId = request.FacilityId,
            ShipmentNumber = shipmentNumber,
            CarrierCode = request.CarrierCode.Trim(),
            CarrierServiceLevel = request.ServiceLevel,
            StagingLocationId = request.StagingLocationId,
            Status = ShipmentStatus.Draft
        };

        _context.Shipments.Add(shipment);

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = request.ClientId,
            FacilityId = request.FacilityId,
            ShipmentId = shipment.Id,
            Action = "ShipmentCreated",
            Details = $"Created shipment '{shipmentNumber}' for carrier '{request.CarrierCode}' ({request.ServiceLevel})",
            UserId = request.UserId,
            TimestampUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created Shipment '{Number}' for Client '{Client}'", shipmentNumber, request.ClientId);

        return new CreateShipmentResult
        {
            IsSuccess = true,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            Status = shipment.Status,
            TotalPackagesCount = 0,
            Message = "Shipment created successfully."
        };
    }

    public async Task<AddPackageToShipmentResult> AddPackageToShipmentAsync(
        AddPackageToShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        if (await HasActiveBolAsync(shipment.Id, cancellationToken))
        {
            throw new InvalidDomainStateException(
                $"Cannot modify manifested contents of shipment '{shipment.ShipmentNumber}' after an active Bill of Lading ('{shipment.BillOfLadingNumber}') has been issued. Void and reissue the Bill of Lading first.");
        }

        var package = await _context.Packages
            .Include(p => p.ShippingLabels)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        // 1. Tenant Isolation Guard
        if (package.ClientId != shipment.ClientId)
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' belongs to a different client tenant. Cross-tenant consolidation rejected.");
        }

        // 2. Facility Guard
        if (package.FacilityId != shipment.FacilityId)
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' belongs to a different facility. Cross-facility consolidation rejected.");
        }

        // 3. Carrier Compatibility Guard
        if (!string.IsNullOrWhiteSpace(shipment.CarrierCode) && !string.IsNullOrWhiteSpace(package.CarrierCode) &&
            !string.Equals(shipment.CarrierCode, package.CarrierCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package carrier '{package.CarrierCode}' is incompatible with shipment carrier '{shipment.CarrierCode}'.");
        }

        // 4. Valid Shipping Label Guard
        var activeLabel = package.ShippingLabels.FirstOrDefault(l => l.Status != ShippingLabelStatus.Voided);
        if (activeLabel == null || string.IsNullOrWhiteSpace(package.TrackingNumber))
        {
            throw new ValidationException(nameof(request.PackageId),
                $"Package '{package.PackageNumber}' does not have a valid, active shipping label. Voided or missing labels rejected.");
        }

        // 5. Duplicate Active Shipment Membership Guard
        if (package.ShipmentId.HasValue && package.ShipmentId.Value != shipment.Id)
        {
            throw new InvalidDomainStateException(
                $"Package '{package.PackageNumber}' already belongs to active shipment '{package.ShipmentId.Value}'. Duplicate shipment membership rejected.");
        }

        // 6. Domain Consolidation (Zero inventory reservation or stock movement)
        shipment.AddPackage(package);

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            PackageId = package.Id,
            Action = "PackageAdded",
            Details = $"Added package '{package.PackageNumber}' (Tracking: {package.TrackingNumber}, Weight: {package.GrossWeightKg}kg)",
            UserId = request.UserId,
            TimestampUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Added Package '{Pkg}' to Shipment '{Shipment}'. New Total: {Count} packages, {Weight}kg",
            package.PackageNumber, shipment.ShipmentNumber, shipment.TotalPackagesCount, shipment.TotalWeightKg);

        return new AddPackageToShipmentResult
        {
            IsSuccess = true,
            ShipmentId = shipment.Id,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            TotalPackagesCount = shipment.TotalPackagesCount,
            TotalGrossWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            Message = "Package consolidated into shipment successfully."
        };
    }

    public async Task<RemovePackageFromShipmentResult> RemovePackageFromShipmentAsync(
        RemovePackageFromShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException(nameof(request.Reason), "A reason must be provided when removing a package from a shipment.");
        }

        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        if (await HasActiveBolAsync(shipment.Id, cancellationToken))
        {
            throw new InvalidDomainStateException(
                $"Cannot remove packages from shipment '{shipment.ShipmentNumber}' after an active Bill of Lading ('{shipment.BillOfLadingNumber}') has been issued. Void and reissue the Bill of Lading first.");
        }

        var package = shipment.Packages.FirstOrDefault(p => p.Id == request.PackageId);
        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        shipment.RemovePackage(package.Id);

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            PackageId = package.Id,
            Action = "PackageRemoved",
            Details = $"Removed package '{package.PackageNumber}'. Reason: '{request.Reason}'",
            UserId = request.UserId,
            TimestampUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Removed Package '{Pkg}' from Shipment '{Shipment}'. Reason: '{Reason}'",
            package.PackageNumber, shipment.ShipmentNumber, request.Reason);

        return new RemovePackageFromShipmentResult
        {
            IsSuccess = true,
            ShipmentId = shipment.Id,
            PackageId = package.Id,
            TotalPackagesCount = shipment.TotalPackagesCount,
            Message = "Package removed from shipment."
        };
    }

    public async Task<StagePackageResult> StagePackageAsync(
        StagePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .Include(p => p.HandlingUnit)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == request.StagingLocationId && !l.IsDeleted, cancellationToken);

        if (location == null)
        {
            throw new EntityNotFoundException(nameof(Location), request.StagingLocationId);
        }

        // Validate staging location eligibility
        if (location.LocationType != LocationType.StagingLane && location.LocationType != LocationType.DockDoor)
        {
            throw new ValidationException(nameof(request.StagingLocationId),
                $"Location '{location.LocationCode}' is a '{location.LocationType}'. Staging is only permitted in StagingLane or DockDoor locations.");
        }

        if (location.FacilityId != package.FacilityId)
        {
            throw new ValidationException(nameof(request.StagingLocationId),
                $"Staging location '{location.LocationCode}' belongs to a different facility.");
        }

        if (!location.IsActive)
        {
            throw new ValidationException(nameof(request.StagingLocationId),
                $"Staging location '{location.LocationCode}' is inactive and cannot receive staged packages.");
        }

        package.Stage(location.Id);

        if (package.ShipmentId.HasValue)
        {
            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                ShipmentId = package.ShipmentId.Value,
                PackageId = package.Id,
                Action = "PackageStaged",
                Details = $"Package '{package.PackageNumber}' staged at location '{location.LocationCode}'",
                UserId = request.UserId,
                TimestampUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Package '{Pkg}' staged at '{Loc}'", package.PackageNumber, location.LocationCode);

        return new StagePackageResult
        {
            IsSuccess = true,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            PackageStatus = package.Status,
            StagingLocationCode = location.LocationCode,
            Message = "Package staged successfully."
        };
    }

    public async Task<LoadPackageResult> LoadPackageAsync(
        LoadPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .Include(p => p.HandlingUnit)
            .Include(p => p.Shipment)
                .ThenInclude(s => s!.Packages)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        // Idempotency: If already loaded, return success idempotently
        if (package.Status == PackageStatus.Loaded)
        {
            return new LoadPackageResult
            {
                IsSuccess = true,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                PackageStatus = package.Status,
                ShipmentStatus = package.Shipment?.Status,
                Message = "Package is already loaded (Idempotent)."
            };
        }

        // Physical Dock Rule: Package must be Staged before Loading!
        if (package.Status != PackageStatus.Staged)
        {
            throw new InvalidDomainStateException(
                $"Cannot load package '{package.PackageNumber}' with status '{package.Status}'. Package must be Staged at dock staging lane before loading onto trailer.");
        }

        // Must be assigned to a shipment
        if (!package.ShipmentId.HasValue)
        {
            throw new InvalidDomainStateException(
                $"Cannot load package '{package.PackageNumber}' without an assigned shipment.");
        }

        if (request.ShipmentId.HasValue && package.ShipmentId.Value != request.ShipmentId.Value)
        {
            throw new ValidationException(nameof(request.ShipmentId),
                $"Package '{package.PackageNumber}' belongs to shipment '{package.ShipmentId.Value}', not '{request.ShipmentId.Value}'.");
        }

        Location? dockLocation = null;
        if (request.DockLocationId.HasValue)
        {
            if (request.DockLocationId.Value == Guid.Empty)
            {
                throw new ValidationException(nameof(request.DockLocationId), "A valid dock location ID is required when provided.");
            }

            dockLocation = await _context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.DockLocationId.Value && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.DockLocationId.Value);

            if (!dockLocation.IsActive)
            {
                throw new ValidationException(nameof(request.DockLocationId),
                    $"Dock location '{dockLocation.LocationCode}' is inactive.");
            }

            if (dockLocation.FacilityId != package.FacilityId)
            {
                throw new ValidationException(nameof(request.DockLocationId),
                    $"Dock location '{dockLocation.LocationCode}' belongs to a different facility.");
            }

            if (dockLocation.LocationType != LocationType.DockDoor && dockLocation.LocationType != LocationType.StagingLane)
            {
                throw new ValidationException(nameof(request.DockLocationId),
                    $"Location '{dockLocation.LocationCode}' is a '{dockLocation.LocationType}'. Loading requires a DockDoor or StagingLane location.");
            }
        }

        package.Load();

        if (dockLocation != null)
        {
            package.CurrentLocationId = dockLocation.Id;
            package.HandlingUnit?.MoveToLocation(dockLocation.Id, HandlingUnitStatus.InTransit);
        }

        var shipment = package.Shipment;
        if (shipment != null)
        {
            if (!string.IsNullOrWhiteSpace(request.TrailerNumber) && string.IsNullOrWhiteSpace(shipment.TrailerNumber))
            {
                shipment.TrailerNumber = request.TrailerNumber.Trim();
            }

            var dockSuffix = dockLocation != null ? $" via dock '{dockLocation.LocationCode}'" : string.Empty;
            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                ShipmentId = shipment.Id,
                PackageId = package.Id,
                Action = "PackageLoaded",
                Details = $"Package '{package.PackageNumber}' loaded onto trailer '{request.TrailerNumber ?? shipment.TrailerNumber}'{dockSuffix}",
                UserId = request.UserId,
                TimestampUtc = DateTime.UtcNow
            });

            // If all packages in shipment are now loaded, advance shipment to ReadyForDispatch
            var allOtherLoaded = await _context.Packages
                .Where(p => p.ShipmentId == shipment.Id && p.Id != package.Id && !p.IsDeleted)
                .AllAsync(p => p.Status == PackageStatus.Loaded, cancellationToken);

            if (allOtherLoaded && shipment.Status == ShipmentStatus.Draft)
            {
                shipment.MarkReadyForDispatch();
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Package '{Pkg}' confirmed Loaded onto trailer", package.PackageNumber);

        return new LoadPackageResult
        {
            IsSuccess = true,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            PackageStatus = package.Status,
            ShipmentStatus = shipment?.Status,
            Message = "Package successfully confirmed Loaded."
        };
    }

    public async Task<GenerateBolResult> GenerateBillOfLadingAsync(
        GenerateBolRequest request,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _context.Shipments
            .Include(s => s.Facility)
            .Include(s => s.Client)
            .Include(s => s.Packages)
                .ThenInclude(p => p.OutboundOrder)
                    .ThenInclude(o => o.ShippingAddress)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        var activePackages = shipment.Packages.Where(p => !p.IsDeleted).ToList();
        if (activePackages.Count == 0)
        {
            throw new InvalidDomainStateException($"Cannot generate Bill of Lading for empty shipment '{shipment.ShipmentNumber}'.");
        }

        // Return preserved immutable BOL snapshot if an active BOL was already issued
        var existingBol = await _context.BillsOfLading
            .FirstOrDefaultAsync(b => b.ShipmentId == shipment.Id && !b.IsVoided && !b.IsDeleted, cancellationToken);

        if (existingBol != null)
        {
            _logger.LogInformation("Returning existing issued Bill of Lading snapshot for Shipment '{Shp}'", shipment.ShipmentNumber);
            return new GenerateBolResult
            {
                IsSuccess = true,
                BolId = existingBol.Id,
                BolNumber = existingBol.BolNumber,
                ShipmentId = shipment.Id,
                TotalPackageCount = existingBol.TotalPackageCount,
                TotalGrossWeightKg = existingBol.TotalGrossWeightKg,
                CarrierCode = existingBol.CarrierCode,
                IssuedAtUtc = existingBol.IssuedAtUtc,
                DocumentSnapshotJson = existingBol.DocumentSnapshotJson,
                Message = "Returned existing issued Bill of Lading (Preserved Snapshot)."
            };
        }

        // P744-R02: Enforce BOL issuance only once all cartons are loaded and shipment is ReadyForDispatch
        if (shipment.Status != ShipmentStatus.ReadyForDispatch ||
            activePackages.Any(p => p.Status != PackageStatus.Loaded))
        {
            throw new InvalidDomainStateException(
                $"Cannot issue Bill of Lading for shipment '{shipment.ShipmentNumber}' with status '{shipment.Status}'. All consolidated packages must be Loaded and shipment ReadyForDispatch before BOL issuance.");
        }

        // P744-R01: Require and build persisted dispatching facility origin address (never fabricate a fallback hub address)
        var facility = shipment.Facility;
        if (string.IsNullOrWhiteSpace(facility.AddressLine1) ||
            string.IsNullOrWhiteSpace(facility.City) ||
            string.IsNullOrWhiteSpace(facility.CountryCode))
        {
            throw new ValidationException(nameof(shipment.Facility),
                $"Dispatching facility '{facility.FacilityCode}' is missing required origin address fields (AddressLine1, City, CountryCode) needed to issue a Bill of Lading.");
        }

        var shipperAddressSnapshot = FormatPostalAddress(
            facility.AddressLine1,
            facility.AddressLine2,
            facility.City,
            facility.StateOrProvince,
            facility.PostalCode,
            facility.CountryCode);

        // P744-R01: Accurately capture single or multi-consignee destination snapshots
        var packageConsignees = activePackages.Select(p =>
        {
            var order = p.OutboundOrder;
            var addr = order.ShippingAddress;
            var recipientName = !string.IsNullOrWhiteSpace(addr?.RecipientName)
                ? addr!.RecipientName.Trim()
                : !string.IsNullOrWhiteSpace(order.CustomerName)
                    ? order.CustomerName.Trim()
                    : "Consignee On File";
            var formattedAddr = addr != null
                ? FormatPostalAddress(addr.AddressLine1, addr.AddressLine2, addr.City, addr.StateOrProvince, addr.PostalCode, addr.CountryCode)
                : !string.IsNullOrWhiteSpace(order.DestinationAddress)
                    ? order.DestinationAddress.Trim()
                    : "Destination Address Pending";

            return new
            {
                Package = p,
                OrderNumber = order.OrderNumber,
                RecipientName = recipientName,
                AddressLine1 = addr?.AddressLine1,
                AddressLine2 = addr?.AddressLine2,
                City = addr?.City,
                StateOrProvince = addr?.StateOrProvince,
                PostalCode = addr?.PostalCode,
                CountryCode = addr?.CountryCode,
                FormattedAddress = formattedAddr
            };
        }).ToList();

        var distinctConsignees = packageConsignees
            .GroupBy(x => $"{x.RecipientName.ToUpperInvariant()}|{x.FormattedAddress.ToUpperInvariant()}")
            .Select(g =>
            {
                var first = g.First();
                return new
                {
                    first.RecipientName,
                    first.AddressLine1,
                    first.AddressLine2,
                    first.City,
                    first.StateOrProvince,
                    first.PostalCode,
                    first.CountryCode,
                    first.FormattedAddress,
                    OrderNumbers = g.Select(x => x.OrderNumber).Distinct().ToList(),
                    PackageCount = g.Count()
                };
            })
            .ToList();

        string consigneeName;
        string consigneeAddressSnapshot;
        object consigneeSnapshotNode;
        if (distinctConsignees.Count == 1)
        {
            var single = distinctConsignees[0];
            consigneeName = single.RecipientName.Length > 200 ? single.RecipientName[..200] : single.RecipientName;
            consigneeAddressSnapshot = single.FormattedAddress;
            consigneeSnapshotNode = new
            {
                Name = single.RecipientName,
                AddressLine1 = single.AddressLine1,
                AddressLine2 = single.AddressLine2,
                City = single.City,
                StateOrProvince = single.StateOrProvince,
                PostalCode = single.PostalCode,
                CountryCode = single.CountryCode,
                Address = single.FormattedAddress,
                IsMultiConsignee = false,
                DestinationCount = 1
            };
        }
        else
        {
            var label = $"Multiple Consignees ({distinctConsignees.Count} Destinations)";
            consigneeName = label.Length > 200 ? label[..200] : label;
            consigneeAddressSnapshot = string.Join(" | ", distinctConsignees.Select(c => $"{c.RecipientName}: {c.FormattedAddress}"));
            consigneeSnapshotNode = new
            {
                Name = consigneeName,
                Address = consigneeAddressSnapshot,
                IsMultiConsignee = true,
                DestinationCount = distinctConsignees.Count
            };
        }

        var priorBolCount = await _context.BillsOfLading
            .AsNoTracking()
            .CountAsync(b => b.ShipmentId == shipment.Id && !b.IsDeleted, cancellationToken);
        var revisionNumber = priorBolCount + 1;
        string bolNumber = revisionNumber == 1
            ? $"BOL-{shipment.ShipmentNumber}"
            : $"BOL-{shipment.ShipmentNumber}-R{revisionNumber}";

        var effectiveTrailer = !string.IsNullOrWhiteSpace(request.TrailerNumber)
            ? request.TrailerNumber.Trim()
            : shipment.TrailerNumber?.Trim();
        var effectiveSeal = !string.IsNullOrWhiteSpace(request.SealNumber)
            ? request.SealNumber.Trim()
            : shipment.SealNumber?.Trim();

        var trackingList = string.Join(", ", activePackages.Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber)).Select(p => p.TrackingNumber));
        var issuedAtUtc = DateTime.UtcNow;

        var snapshotData = new
        {
            BolNumber = bolNumber,
            RevisionNumber = revisionNumber,
            ShipmentNumber = shipment.ShipmentNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierServiceLevel = shipment.CarrierServiceLevel.ToString(),
            TrailerNumber = effectiveTrailer,
            SealNumber = effectiveSeal,
            TotalPackages = shipment.TotalPackagesCount,
            TotalWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            Shipper = new
            {
                Name = facility.Name,
                Code = facility.FacilityCode,
                AddressLine1 = facility.AddressLine1,
                AddressLine2 = facility.AddressLine2,
                City = facility.City,
                StateOrProvince = facility.StateOrProvince,
                PostalCode = facility.PostalCode,
                CountryCode = facility.CountryCode,
                Address = shipperAddressSnapshot
            },
            Consignee = consigneeSnapshotNode,
            Consignees = distinctConsignees,
            Packages = packageConsignees.Select(pc => new
            {
                PackageNumber = pc.Package.PackageNumber,
                OrderNumber = pc.OrderNumber,
                TrackingNumber = pc.Package.TrackingNumber,
                WeightKg = pc.Package.GrossWeightKg,
                Dimensions = $"{pc.Package.LengthCm}x{pc.Package.WidthCm}x{pc.Package.HeightCm} cm",
                ConsigneeName = pc.RecipientName,
                ConsigneeAddress = pc.FormattedAddress
            }).ToList(),
            IssuedAtUtc = issuedAtUtc
        };

        var bol = new BillOfLading
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            BolNumber = bolNumber,
            RevisionNumber = revisionNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierName = shipment.CarrierCode,
            ServiceLevel = shipment.CarrierServiceLevel.ToString(),
            TrailerNumber = effectiveTrailer,
            SealNumber = effectiveSeal,
            TotalPackageCount = shipment.TotalPackagesCount,
            TotalGrossWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            SpecialInstructions = request.SpecialInstructions,
            ShipperName = facility.Name,
            ShipperAddressSnapshot = shipperAddressSnapshot,
            ConsigneeName = consigneeName,
            ConsigneeAddressSnapshot = consigneeAddressSnapshot,
            PackageTrackingNumbers = trackingList,
            DocumentSnapshotJson = JsonSerializer.Serialize(snapshotData),
            PreparedByUserId = request.UserId,
            IssuedAtUtc = issuedAtUtc
        };

        _context.BillsOfLading.Add(bol);
        shipment.BillOfLadingNumber = bol.BolNumber;
        if (!string.IsNullOrWhiteSpace(effectiveTrailer)) shipment.TrailerNumber = effectiveTrailer;
        if (!string.IsNullOrWhiteSpace(effectiveSeal)) shipment.SealNumber = effectiveSeal;

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            Action = "BillOfLadingIssued",
            Details = $"Generated BOL '{bolNumber}' (rev {revisionNumber}) with {shipment.TotalPackagesCount} packages, {shipment.TotalWeightKg}kg",
            UserId = request.UserId,
            TimestampUtc = issuedAtUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Issued Bill of Lading '{BOL}' for Shipment '{Shp}'", bol.BolNumber, shipment.ShipmentNumber);

        return new GenerateBolResult
        {
            IsSuccess = true,
            BolId = bol.Id,
            BolNumber = bol.BolNumber,
            ShipmentId = shipment.Id,
            TotalPackageCount = bol.TotalPackageCount,
            TotalGrossWeightKg = bol.TotalGrossWeightKg,
            CarrierCode = bol.CarrierCode,
            IssuedAtUtc = bol.IssuedAtUtc,
            DocumentSnapshotJson = bol.DocumentSnapshotJson,
            Message = "Bill of Lading issued successfully."
        };
    }

    public async Task<VoidBolResult> VoidBillOfLadingAsync(
        VoidBolRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException(nameof(request.Reason), "A reason must be provided when voiding a Bill of Lading.");
        }

        var bol = await _context.BillsOfLading
            .Include(b => b.Shipment)
            .FirstOrDefaultAsync(b => b.Id == request.BolId && !b.IsDeleted, cancellationToken);

        if (bol == null)
        {
            throw new EntityNotFoundException(nameof(BillOfLading), request.BolId);
        }

        if (bol.Shipment.Status == ShipmentStatus.Dispatched)
        {
            throw new InvalidDomainStateException($"Cannot void Bill of Lading '{bol.BolNumber}' after shipment '{bol.Shipment.ShipmentNumber}' has been dispatched.");
        }

        bol.Void(request.Reason.Trim());
        if (string.Equals(bol.Shipment.BillOfLadingNumber, bol.BolNumber, StringComparison.Ordinal))
        {
            bol.Shipment.BillOfLadingNumber = null;
        }

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = bol.ClientId,
            FacilityId = bol.FacilityId,
            ShipmentId = bol.ShipmentId,
            Action = "BillOfLadingVoided",
            Details = $"Voided BOL '{bol.BolNumber}'. Reason: '{request.Reason.Trim()}'",
            UserId = request.UserId,
            TimestampUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new VoidBolResult
        {
            IsSuccess = true,
            BolId = bol.Id,
            BolNumber = bol.BolNumber,
            ShipmentId = bol.ShipmentId,
            Message = $"Bill of Lading '{bol.BolNumber}' voided."
        };
    }

    public async Task<ConfirmDispatchCustodyResult> ConfirmDispatchAsync(
        ConfirmDispatchCustodyRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Authorization Guard
        if (request.AuthorizedUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.AuthorizedUserId),
                "An authorized user ID must be provided to confirm carrier custody transfer.");
        }

        var normalizedIdempKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        return await _context.ExecuteWithShipmentLockAsync(request.ShipmentId, async () =>
        {
            var shipment = await _context.Shipments
                .Include(s => s.Packages)
                .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

            if (shipment == null)
            {
                throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
            }

            await using var idempotencyLock = normalizedIdempKey != null
                ? await _context.AcquireScopedIdempotencyLockAsync("DISPATCH", shipment.FacilityId, normalizedIdempKey, cancellationToken)
                : null;

            // 2. P744-R03 Idempotency & Payload Consistency Check on Already-Dispatched Shipments
            if (shipment.Status == ShipmentStatus.Dispatched)
            {
                var recordedCustody = await _context.ShipmentCustodyTransfers
                    .AsNoTracking()
                    .Where(c => c.ShipmentId == shipment.Id)
                    .OrderByDescending(c => c.TransferredAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                var dispatchAudit = await _context.ShipmentAuditLogs
                    .AsNoTracking()
                    .Where(a => a.ShipmentId == shipment.Id && a.Action == "CustodyTransferredAndDispatched")
                    .OrderByDescending(a => a.TimestampUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                var recordedIdempKey = ExtractMarkerValue(dispatchAudit?.Details, "DISPATCH_IDEMP");
                var effectiveRetryDriverId = request.AssignedDriverUserId ?? recordedCustody?.AssignedDriverUserId ?? shipment.AssignedDriverUserId;
                var effectiveRetryTrailer = !string.IsNullOrWhiteSpace(request.TrailerNumber)
                    ? request.TrailerNumber.Trim()
                    : recordedCustody?.TrailerNumber ?? shipment.TrailerNumber;
                var effectiveRetrySeal = !string.IsNullOrWhiteSpace(request.SealNumber)
                    ? request.SealNumber.Trim()
                    : recordedCustody?.SealNumber ?? shipment.SealNumber;

                var idempMatches = string.Equals(recordedIdempKey, normalizedIdempKey, StringComparison.Ordinal);
                var custodyMatches = recordedCustody != null &&
                    string.Equals(recordedCustody.DriverName.Trim(), (request.DriverName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(recordedCustody.DriverSignatureAcknowledgment.Trim(), (request.DriverSignature ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                    string.Equals(recordedCustody.DriverLicenseNumber?.Trim() ?? string.Empty, request.DriverLicenseNumber?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(recordedCustody.TrailerNumber?.Trim() ?? string.Empty, effectiveRetryTrailer?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(recordedCustody.SealNumber?.Trim() ?? string.Empty, effectiveRetrySeal?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    recordedCustody.AssignedDriverUserId == effectiveRetryDriverId;

                if (!idempMatches || !custodyMatches)
                {
                    throw new ConcurrencyException(
                        $"Shipment '{shipment.ShipmentNumber}' was already dispatched with different custody or idempotency parameters.");
                }

                var pkgOrderIds = shipment.Packages.Select(p => p.OutboundOrderId).Distinct().ToList();
                var alreadyCompletedOrderIds = pkgOrderIds.Count == 0
                    ? new List<Guid>()
                    : await _context.OutboundOrders
                        .AsNoTracking()
                        .Where(o => pkgOrderIds.Contains(o.Id) && o.Status == OutboundOrderStatus.Completed)
                        .Select(o => o.Id)
                        .ToListAsync(cancellationToken);

                _logger.LogInformation("Idempotent hit: Shipment '{Number}' is already dispatched.", shipment.ShipmentNumber);
                return new ConfirmDispatchCustodyResult
                {
                    IsSuccess = true,
                    ShipmentId = shipment.Id,
                    ShipmentNumber = shipment.ShipmentNumber,
                    Status = shipment.Status,
                    DispatchedAtUtc = recordedCustody?.TransferredAtUtc ?? shipment.DispatchedAtUtc ?? DateTime.UtcNow,
                    TotalPackagesDispatched = shipment.TotalPackagesCount,
                    TotalWeightKg = shipment.TotalWeightKg,
                    DriverName = recordedCustody!.DriverName,
                    AssignedDriverUserId = recordedCustody.AssignedDriverUserId,
                    BillOfLadingNumber = recordedCustody.BillOfLadingNumber ?? shipment.BillOfLadingNumber,
                    CompletedOrderIds = alreadyCompletedOrderIds,
                    Message = "Shipment already dispatched (Idempotent)."
                };
            }

            // P744-R03: Reject reuse of an IdempotencyKey across different shipments in the same facility
            if (normalizedIdempKey != null)
            {
                var idempMarker = $"[DISPATCH_IDEMP:{normalizedIdempKey}]";
                var usedOnOtherShipment = await _context.ShipmentAuditLogs
                    .AsNoTracking()
                    .AnyAsync(a => a.FacilityId == shipment.FacilityId &&
                                   a.Action == "CustodyTransferredAndDispatched" &&
                                   a.ShipmentId != shipment.Id &&
                                   a.Details.Contains(idempMarker), cancellationToken);

                if (usedOnOtherShipment)
                {
                    throw new ConcurrencyException(
                        $"Dispatch idempotency key '{normalizedIdempKey}' has already been consumed by a different shipment in this facility.");
                }
            }

            if (shipment.Packages.Count == 0)
            {
                throw new InvalidDomainStateException($"Cannot dispatch empty shipment '{shipment.ShipmentNumber}'.");
            }

            // 3. Physical Loading Check: Every package MUST be confirmed Loaded before dispatch!
            var unLoadedPackages = shipment.Packages
                .Where(p => p.Status != PackageStatus.Loaded)
                .ToList();

            if (unLoadedPackages.Count > 0)
            {
                var unLoadedNums = string.Join(", ", unLoadedPackages.Select(p => $"'{p.PackageNumber}' ({p.Status})"));
                throw new InvalidDomainStateException(
                    $"Cannot dispatch shipment '{shipment.ShipmentNumber}'. All packages must be confirmed Loaded onto trailer before custody transfer. Unloaded: {unLoadedNums}.");
            }

            if (shipment.Status != ShipmentStatus.ReadyForDispatch)
            {
                throw new InvalidDomainStateException(
                    $"Cannot dispatch shipment '{shipment.ShipmentNumber}' with status '{shipment.Status}'. Shipment must be ReadyForDispatch.");
            }

            // P744-R02: Active issued BOL is mandatory and trailer/seal values must match the immutable BOL
            var activeBol = await _context.BillsOfLading
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ShipmentId == shipment.Id && !b.IsVoided && !b.IsDeleted, cancellationToken);

            if (activeBol == null)
            {
                throw new InvalidDomainStateException(
                    $"Cannot dispatch shipment '{shipment.ShipmentNumber}' without an active issued Bill of Lading.");
            }

            if (!string.IsNullOrWhiteSpace(request.TrailerNumber) &&
                !string.Equals(request.TrailerNumber.Trim(), activeBol.TrailerNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainStateException(
                    $"Trailer number '{request.TrailerNumber.Trim()}' conflicts with issued Bill of Lading '{activeBol.BolNumber}' trailer '{activeBol.TrailerNumber ?? "(none)"}'. Void and reissue the BOL to change trailer details.");
            }

            if (!string.IsNullOrWhiteSpace(request.SealNumber) &&
                !string.Equals(request.SealNumber.Trim(), activeBol.SealNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDomainStateException(
                    $"Seal number '{request.SealNumber.Trim()}' conflicts with issued Bill of Lading '{activeBol.BolNumber}' seal '{activeBol.SealNumber ?? "(none)"}'. Void and reissue the BOL to change seal details.");
            }

            // Authoritative Driver Identity Binding
            Guid? effectiveAssignedDriverUserId = request.AssignedDriverUserId ?? shipment.AssignedDriverUserId;
            if (request.AssignedDriverUserId.HasValue)
            {
                if (shipment.AssignedDriverUserId.HasValue &&
                    shipment.AssignedDriverUserId.Value != request.AssignedDriverUserId.Value)
                {
                    throw new InvalidDomainStateException(
                        $"Shipment '{shipment.ShipmentNumber}' is already assigned to driver '{shipment.AssignedDriverUserId.Value}'. Reassign via dispatch management before confirming custody transfer for a different driver.");
                }

                await OutboundOperationalAuthorization.ValidateAssignableDriverAsync(
                    _context,
                    request.AssignedDriverUserId.Value,
                    shipment.ClientId,
                    shipment.FacilityId,
                    cancellationToken);
            }
            else if (effectiveAssignedDriverUserId.HasValue)
            {
                await OutboundOperationalAuthorization.ValidateAssignableDriverAsync(
                    _context,
                    effectiveAssignedDriverUserId.Value,
                    shipment.ClientId,
                    shipment.FacilityId,
                    cancellationToken);
            }

            // Driver details validation
            if (string.IsNullOrWhiteSpace(request.DriverName))
            {
                throw new ValidationException(nameof(request.DriverName), "Carrier driver name is required for custody transfer.");
            }

            if (string.IsNullOrWhiteSpace(request.DriverSignature))
            {
                throw new ValidationException(nameof(request.DriverSignature), "Driver signature acknowledgment is required.");
            }

            var effectiveTrailer = !string.IsNullOrWhiteSpace(request.TrailerNumber)
                ? request.TrailerNumber.Trim()
                : activeBol.TrailerNumber ?? shipment.TrailerNumber;
            var effectiveSeal = !string.IsNullOrWhiteSpace(request.SealNumber)
                ? request.SealNumber.Trim()
                : activeBol.SealNumber ?? shipment.SealNumber;

            // 4. Record Immutable Custody Transfer
            var custody = new ShipmentCustodyTransfer
            {
                ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId,
                ShipmentId = shipment.Id,
                CarrierCode = shipment.CarrierCode,
                DriverName = request.DriverName.Trim(),
                DriverLicenseNumber = request.DriverLicenseNumber?.Trim(),
                DriverSignatureAcknowledgment = request.DriverSignature.Trim(),
                TrailerNumber = effectiveTrailer,
                SealNumber = effectiveSeal,
                BillOfLadingNumber = activeBol.BolNumber,
                TotalPackagesConfirmed = shipment.TotalPackagesCount,
                TotalWeightKg = shipment.TotalWeightKg,
                AuthorizedByUserId = request.AuthorizedUserId,
                AssignedDriverUserId = effectiveAssignedDriverUserId,
                CustodyStatus = ShipmentCustodyStatus.Confirmed,
                TransferredAtUtc = DateTime.UtcNow,
                Notes = request.Notes
            };
            _context.ShipmentCustodyTransfers.Add(custody);

            // 5. Update Shipment to Dispatched (Zero physical inventory quantity modification)
            shipment.Status = ShipmentStatus.Dispatched;
            shipment.AssignedDriverUserId = effectiveAssignedDriverUserId;
            shipment.CustodyStatus = effectiveAssignedDriverUserId.HasValue
                ? ShipmentCustodyStatus.Confirmed
                : ShipmentCustodyStatus.Unassigned;
            shipment.TrailerNumber = custody.TrailerNumber;
            shipment.SealNumber = custody.SealNumber;
            shipment.BillOfLadingNumber = activeBol.BolNumber;
            shipment.DispatchedAtUtc = custody.TransferredAtUtc;
            shipment.Version++;

            var fp = ComputeDispatchFingerprint(
                shipment.Id,
                custody.DriverName,
                custody.DriverLicenseNumber,
                custody.DriverSignatureAcknowledgment,
                custody.TrailerNumber,
                custody.SealNumber,
                effectiveAssignedDriverUserId);
            var idempAuditTag = normalizedIdempKey != null ? $" [DISPATCH_IDEMP:{normalizedIdempKey}]" : string.Empty;

            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = shipment.ClientId,
                FacilityId = shipment.FacilityId,
                ShipmentId = shipment.Id,
                Action = "CustodyTransferredAndDispatched",
                Details = $"Handed over to driver '{custody.DriverName}' (DriverUserId: {effectiveAssignedDriverUserId?.ToString() ?? "External"}, Carrier: {shipment.CarrierCode}, Trailer: {custody.TrailerNumber}, Seal: {custody.SealNumber}){idempAuditTag} [FP:{fp}]",
                UserId = request.AuthorizedUserId,
                TimestampUtc = custody.TransferredAtUtc
            });

            // 6. Check Associated Outbound Orders (Support Partial Order Dispatch)
            var completedOrders = new List<Guid>();
            var orderIds = shipment.Packages.Select(p => p.OutboundOrderId).Distinct().ToList();

            foreach (var orderId in orderIds)
            {
                var order = await _context.OutboundOrders
                    .Include(o => o.Packages)
                        .ThenInclude(p => p.Shipment)
                    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

                if (order != null)
                {
                    // Order is completed ONLY if ALL packages of this order have been dispatched
                    var allDispatched = order.Packages.Count > 0 && order.Packages.All(p => p.Status == PackageStatus.Loaded &&
                        ((p.ShipmentId == shipment.Id) || (p.Shipment != null && p.Shipment.Status == ShipmentStatus.Dispatched)));

                    if (allDispatched)
                    {
                        order.Status = OutboundOrderStatus.Completed;
                        completedOrders.Add(order.Id);
                        _logger.LogInformation("Order '{Order}' completed after all packages dispatched", order.OrderNumber);
                    }
                    else
                    {
                        _logger.LogInformation("Order '{Order}' remains InProgress (Partial order shipment)", order.OrderNumber);
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Shipment '{Shipment}' successfully dispatched. Custody transferred to '{Driver}'. BOL: '{BOL}'",
                shipment.ShipmentNumber, custody.DriverName, shipment.BillOfLadingNumber);

            return new ConfirmDispatchCustodyResult
            {
                IsSuccess = true,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                Status = shipment.Status,
                DispatchedAtUtc = custody.TransferredAtUtc,
                TotalPackagesDispatched = shipment.TotalPackagesCount,
                TotalWeightKg = shipment.TotalWeightKg,
                DriverName = custody.DriverName,
                AssignedDriverUserId = custody.AssignedDriverUserId,
                BillOfLadingNumber = custody.BillOfLadingNumber,
                CompletedOrderIds = completedOrders,
                Message = "Custody transfer confirmed and shipment dispatched successfully."
            };
        }, cancellationToken);
    }

    private Task<bool> HasActiveBolAsync(Guid shipmentId, CancellationToken cancellationToken)
        => _context.BillsOfLading
            .AsNoTracking()
            .AnyAsync(b => b.ShipmentId == shipmentId && !b.IsVoided && !b.IsDeleted, cancellationToken);

    private static string FormatPostalAddress(
        string? line1,
        string? line2,
        string? city,
        string? stateOrProvince,
        string? postalCode,
        string? countryCode)
    {
        var regionPart = string.Join(" ", new[] { stateOrProvince?.Trim(), postalCode?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var segments = new[]
        {
            line1?.Trim(),
            line2?.Trim(),
            city?.Trim(),
            regionPart,
            countryCode?.Trim()
        }.Where(s => !string.IsNullOrWhiteSpace(s));

        return string.Join(", ", segments);
    }

    private static string ComputeDispatchFingerprint(
        Guid shipmentId,
        string driverName,
        string? driverLicense,
        string driverSignature,
        string? trailerNumber,
        string? sealNumber,
        Guid? assignedDriverUserId)
    {
        var canonical = string.Join("|",
            shipmentId.ToString("N"),
            driverName.Trim().ToUpperInvariant(),
            driverLicense?.Trim().ToUpperInvariant() ?? string.Empty,
            driverSignature.Trim(),
            trailerNumber?.Trim().ToUpperInvariant() ?? string.Empty,
            sealNumber?.Trim().ToUpperInvariant() ?? string.Empty,
            assignedDriverUserId?.ToString("N") ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static string? ExtractMarkerValue(string? details, string markerName)
    {
        if (string.IsNullOrEmpty(details)) return null;
        var prefix = $"[{markerName}:";
        var start = details.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;
        start += prefix.Length;
        var end = details.IndexOf(']', start);
        return end > start ? details[start..end] : null;
    }
}
