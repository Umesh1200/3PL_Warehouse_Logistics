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

        package.Load();

        var shipment = package.Shipment;
        if (shipment != null)
        {
            if (!string.IsNullOrWhiteSpace(request.TrailerNumber) && string.IsNullOrWhiteSpace(shipment.TrailerNumber))
            {
                shipment.TrailerNumber = request.TrailerNumber.Trim();
            }

            _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
            {
                ClientId = package.ClientId,
                FacilityId = package.FacilityId,
                ShipmentId = shipment.Id,
                PackageId = package.Id,
                Action = "PackageLoaded",
                Details = $"Package '{package.PackageNumber}' loaded onto trailer '{request.TrailerNumber ?? shipment.TrailerNumber}'",
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

        if (shipment.Packages.Count == 0)
        {
            throw new InvalidDomainStateException($"Cannot generate Bill of Lading for empty shipment '{shipment.ShipmentNumber}'.");
        }

        // Check if an active issued BOL already exists
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

        string bolNumber = $"BOL-{shipment.ShipmentNumber}";
        var firstDest = shipment.Packages.First().OutboundOrder.ShippingAddress;
        var trackingList = string.Join(", ", shipment.Packages.Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber)).Select(p => p.TrackingNumber));

        var snapshotData = new
        {
            BolNumber = bolNumber,
            ShipmentNumber = shipment.ShipmentNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierServiceLevel = shipment.CarrierServiceLevel.ToString(),
            TrailerNumber = request.TrailerNumber ?? shipment.TrailerNumber,
            SealNumber = request.SealNumber ?? shipment.SealNumber,
            TotalPackages = shipment.TotalPackagesCount,
            TotalWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            Shipper = new
            {
                Name = shipment.Facility.Name,
                Code = shipment.Facility.FacilityCode,
                Address = "100 Distribution Way, Columbus, OH 43215"
            },
            Consignee = new
            {
                Name = firstDest?.RecipientName ?? "Various Outbound Consignees",
                Address = firstDest?.AddressLine1 ?? "Delivery Destination",
                City = firstDest?.City ?? "Springfield",
                State = firstDest?.StateOrProvince ?? "IL",
                Zip = firstDest?.PostalCode ?? "62701"
            },
            Packages = shipment.Packages.Select(p => new
            {
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                WeightKg = p.GrossWeightKg,
                Dimensions = $"{p.LengthCm}x{p.WidthCm}x{p.HeightCm} cm"
            }).ToList(),
            IssuedAtUtc = DateTime.UtcNow
        };

        var bol = new BillOfLading
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            BolNumber = bolNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierName = shipment.CarrierCode,
            ServiceLevel = shipment.CarrierServiceLevel.ToString(),
            TrailerNumber = request.TrailerNumber ?? shipment.TrailerNumber,
            SealNumber = request.SealNumber ?? shipment.SealNumber,
            TotalPackageCount = shipment.TotalPackagesCount,
            TotalGrossWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            SpecialInstructions = request.SpecialInstructions,
            ShipperName = shipment.Facility.Name,
            ShipperAddressSnapshot = "100 Distribution Way, Columbus, OH 43215",
            ConsigneeName = firstDest?.RecipientName ?? "Outbound Consignee",
            ConsigneeAddressSnapshot = firstDest != null
                ? $"{firstDest.AddressLine1}, {firstDest.City}, {firstDest.StateOrProvince} {firstDest.PostalCode}"
                : "Delivery Destination on File",
            PackageTrackingNumbers = trackingList,
            DocumentSnapshotJson = JsonSerializer.Serialize(snapshotData),
            PreparedByUserId = request.UserId,
            IssuedAtUtc = DateTime.UtcNow
        };

        _context.BillsOfLading.Add(bol);
        shipment.BillOfLadingNumber = bol.BolNumber;
        if (!string.IsNullOrWhiteSpace(request.TrailerNumber)) shipment.TrailerNumber = request.TrailerNumber;
        if (!string.IsNullOrWhiteSpace(request.SealNumber)) shipment.SealNumber = request.SealNumber;

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            Action = "BillOfLadingIssued",
            Details = $"Generated BOL '{bolNumber}' with {shipment.TotalPackagesCount} packages, {shipment.TotalWeightKg}kg",
            UserId = request.UserId,
            TimestampUtc = DateTime.UtcNow
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

        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), request.ShipmentId);
        }

        // 2. Idempotency Check: Repeated dispatch confirmation returns existing state cleanly
        if (shipment.Status == ShipmentStatus.Dispatched)
        {
            _logger.LogInformation("Idempotent hit: Shipment '{Number}' is already dispatched.", shipment.ShipmentNumber);
            return new ConfirmDispatchCustodyResult
            {
                IsSuccess = true,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                Status = shipment.Status,
                DispatchedAtUtc = shipment.DispatchedAtUtc ?? DateTime.UtcNow,
                TotalPackagesDispatched = shipment.TotalPackagesCount,
                TotalWeightKg = shipment.TotalWeightKg,
                DriverName = request.DriverName,
                AssignedDriverUserId = shipment.AssignedDriverUserId,
                BillOfLadingNumber = shipment.BillOfLadingNumber,
                Message = "Shipment already dispatched (Idempotent)."
            };
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
            TrailerNumber = request.TrailerNumber ?? shipment.TrailerNumber,
            SealNumber = request.SealNumber ?? shipment.SealNumber,
            BillOfLadingNumber = shipment.BillOfLadingNumber,
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
        shipment.DispatchedAtUtc = custody.TransferredAtUtc;
        shipment.Version++;

        _context.ShipmentAuditLogs.Add(new ShipmentAuditLog
        {
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentId = shipment.Id,
            Action = "CustodyTransferredAndDispatched",
            Details = $"Handed over to driver '{request.DriverName}' (DriverUserId: {effectiveAssignedDriverUserId?.ToString() ?? "External"}, Carrier: {shipment.CarrierCode}, Trailer: {custody.TrailerNumber}, Seal: {custody.SealNumber})",
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
            shipment.ShipmentNumber, request.DriverName, shipment.BillOfLadingNumber);

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
    }
}
