using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Operational shipping service orchestrating carrier rate quotes, label generation, and voiding.
/// Strictly enforces decoupled execution: carrier API calls execute OUTSIDE database transactions.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation (Step 3A).
/// </summary>
public class ShippingLabelService : IShippingLabelService
{
    private readonly IApplicationDbContext _context;
    private readonly IShippingCarrierService _carrierService;
    private readonly ILogger<ShippingLabelService> _logger;

    public ShippingLabelService(
        IApplicationDbContext context,
        IShippingCarrierService carrierService,
        ILogger<ShippingLabelService> logger)
    {
        _context = context;
        _carrierService = carrierService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CarrierRateQuote>> GetRatesForPackageAsync(
        GetRatesForPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .Include(p => p.OutboundOrder)
                .ThenInclude(o => o.ShippingAddress)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        var dest = package.OutboundOrder.ShippingAddress;
        var rateRequest = new CarrierRateRequest
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PreferredCarrierCode = request.PreferredCarrierCode,
            PreferredServiceLevel = request.PreferredServiceLevel,
            OriginAddress = new CarrierAddress
            {
                Name = "Warehouse 3PL Logistics",
                StreetLine1 = "100 Distribution Way",
                City = "Columbus",
                StateOrProvince = "OH",
                PostalCode = "43215",
                CountryCode = "US"
            },
            DestinationAddress = new CarrierAddress
            {
                Name = dest?.RecipientName ?? "Valued Customer",
                CompanyName = dest?.CompanyName,
                StreetLine1 = dest?.AddressLine1 ?? "123 Main St",
                StreetLine2 = dest?.AddressLine2,
                City = dest?.City ?? "Springfield",
                StateOrProvince = dest?.StateOrProvince ?? "IL",
                PostalCode = dest?.PostalCode ?? "62701",
                CountryCode = dest?.CountryCode ?? "US",
                Phone = dest?.PhoneNumber
            },
            Parcel = new CarrierParcel
            {
                PackageNumber = package.PackageNumber,
                LengthCm = package.LengthCm > 0 ? package.LengthCm : 30m,
                WidthCm = package.WidthCm > 0 ? package.WidthCm : 20m,
                HeightCm = package.HeightCm > 0 ? package.HeightCm : 15m,
                WeightKg = package.GrossWeightKg > 0 ? package.GrossWeightKg : 1.0m
            }
        };

        return await _carrierService.GetRatesAsync(rateRequest, cancellationToken);
    }

    public async Task<GenerateShippingLabelResult> GenerateLabelAsync(
        GenerateShippingLabelRequest request,
        CancellationToken cancellationToken = default)
    {
        // -------------------------------------------------------------
        // STEP 1: Validate Package and Eligibility
        // -------------------------------------------------------------
        var package = await _context.Packages
            .Include(p => p.OutboundOrder)
                .ThenInclude(o => o.ShippingAddress)
            .Include(p => p.ShippingLabels)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), request.PackageId);
        }

        if (package.Status != PackageStatus.Packed && package.Status != PackageStatus.LabelPrinted)
        {
            throw new InvalidDomainStateException(
                $"Cannot generate shipping label for package '{package.PackageNumber}' with status '{package.Status}'. Package must be Packed.");
        }

        string idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"IDEMP-{package.Id}-{request.CarrierCode}-{request.ServiceLevel}"
            : request.IdempotencyKey.Trim();

        // Check for existing active label or matching idempotency key
        var activeLabel = package.ShippingLabels.FirstOrDefault(l => l.Status != ShippingLabelStatus.Voided);
        if (activeLabel != null)
        {
            // If already generated for this exact request/idempotency key, return existing without re-calling carrier
            var existingLog = await _context.CarrierOperationLogs
                .FirstOrDefaultAsync(l => l.PackageId == package.Id &&
                                          l.IdempotencyKey == idempotencyKey &&
                                          l.Status == "Success", cancellationToken);

            if (existingLog != null)
            {
                _logger.LogInformation("Idempotent hit: Returning existing label for Package '{Pkg}'", package.PackageNumber);
                return new GenerateShippingLabelResult
                {
                    IsSuccess = true,
                    PackageId = package.Id,
                    PackageNumber = package.PackageNumber,
                    LabelId = activeLabel.Id,
                    TrackingNumber = activeLabel.TrackingNumber,
                    CarrierCode = activeLabel.CarrierCode,
                    ServiceLevel = activeLabel.ServiceLevel,
                    LabelFormat = activeLabel.LabelFormat,
                    LabelData = activeLabel.LabelData,
                    RoutingCode = activeLabel.RoutingCode,
                    PackageStatus = package.Status,
                    LabelStatus = activeLabel.Status,
                    IsSimulated = true,
                    Message = "Returned existing shipping label (Idempotent)."
                };
            }

            throw new InvalidDomainStateException(
                $"Package '{package.PackageNumber}' already has an active shipping label with tracking '{activeLabel.TrackingNumber}'. Void the existing label first.");
        }

        // -------------------------------------------------------------
        // STEP 2 & 3: Persist Pending Carrier Operation & Commit
        // (Ensures carrier request is logged BEFORE external call)
        // -------------------------------------------------------------
        var dest = package.OutboundOrder.ShippingAddress;
        var carrierReq = new CarrierShipmentRequest
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            PackageNumber = package.PackageNumber,
            CarrierCode = request.CarrierCode,
            ServiceLevel = request.ServiceLevel,
            LabelFormat = request.LabelFormat,
            IdempotencyKey = idempotencyKey,
            SimulateFailure = request.SimulateFailure,
            OriginAddress = new CarrierAddress
            {
                Name = "Warehouse 3PL Logistics",
                StreetLine1 = "100 Distribution Way",
                City = "Columbus",
                StateOrProvince = "OH",
                PostalCode = "43215",
                CountryCode = "US"
            },
            DestinationAddress = new CarrierAddress
            {
                Name = dest?.RecipientName ?? "Valued Customer",
                CompanyName = dest?.CompanyName,
                StreetLine1 = dest?.AddressLine1 ?? "123 Main St",
                StreetLine2 = dest?.AddressLine2,
                City = dest?.City ?? "Springfield",
                StateOrProvince = dest?.StateOrProvince ?? "IL",
                PostalCode = dest?.PostalCode ?? "62701",
                CountryCode = dest?.CountryCode ?? "US",
                Phone = dest?.PhoneNumber
            },
            Parcel = new CarrierParcel
            {
                PackageNumber = package.PackageNumber,
                LengthCm = package.LengthCm > 0 ? package.LengthCm : 30m,
                WidthCm = package.WidthCm > 0 ? package.WidthCm : 20m,
                HeightCm = package.HeightCm > 0 ? package.HeightCm : 15m,
                WeightKg = package.GrossWeightKg > 0 ? package.GrossWeightKg : 1.0m
            }
        };

        var opLog = new CarrierOperationLog
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            OperationType = "GenerateLabel",
            CarrierCode = request.CarrierCode,
            ServiceLevel = request.ServiceLevel.ToString(),
            IdempotencyKey = idempotencyKey,
            Status = "Pending",
            RequestPayload = JsonSerializer.Serialize(carrierReq),
            StartedAtUtc = DateTime.UtcNow
        };

        _context.CarrierOperationLogs.Add(opLog);
        await _context.SaveChangesAsync(cancellationToken);

        // -------------------------------------------------------------
        // STEP 4: Call Carrier Service OUTSIDE Database Transaction
        // -------------------------------------------------------------
        CarrierShipmentResponse carrierResponse;
        try
        {
            carrierResponse = await _carrierService.CreateShipmentAsync(carrierReq, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected carrier exception for Package '{Pkg}'", package.PackageNumber);
            carrierResponse = new CarrierShipmentResponse
            {
                IsSuccess = false,
                CarrierCode = request.CarrierCode,
                ServiceLevel = request.ServiceLevel,
                ErrorMessage = $"Carrier communication error: {ex.Message}"
            };
        }

        // -------------------------------------------------------------
        // STEP 5: Persist Carrier Response in New DB Step
        // -------------------------------------------------------------
        if (carrierResponse.IsSuccess)
        {
            opLog.MarkSuccess(JsonSerializer.Serialize(carrierResponse), carrierResponse.TrackingNumber);

            var shippingLabel = new ShippingLabel
            {
                PackageId = package.Id,
                TrackingNumber = carrierResponse.TrackingNumber,
                CarrierCode = carrierResponse.CarrierCode,
                ServiceLevel = carrierResponse.ServiceLevel,
                LabelFormat = carrierResponse.LabelFormat,
                LabelData = carrierResponse.LabelData,
                RoutingCode = carrierResponse.RoutingCode,
                Status = ShippingLabelStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow
            };

            _context.ShippingLabels.Add(shippingLabel);

            // Update package status to LabelPrinted
            package.SetShippingLabel(carrierResponse.TrackingNumber, carrierResponse.CarrierCode, carrierResponse.ServiceLevel.ToString());

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully generated shipping label for Package '{Pkg}': Tracking '{Tracking}', Format '{Format}'",
                package.PackageNumber, carrierResponse.TrackingNumber, carrierResponse.LabelFormat);

            return new GenerateShippingLabelResult
            {
                IsSuccess = true,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                LabelId = shippingLabel.Id,
                TrackingNumber = carrierResponse.TrackingNumber,
                CarrierCode = carrierResponse.CarrierCode,
                ServiceLevel = carrierResponse.ServiceLevel,
                LabelFormat = carrierResponse.LabelFormat,
                LabelData = carrierResponse.LabelData,
                RoutingCode = carrierResponse.RoutingCode,
                RateCharged = carrierResponse.RateCharged,
                PackageStatus = package.Status,
                LabelStatus = shippingLabel.Status,
                IsSimulated = true,
                Message = "Shipping label generated successfully."
            };
        }
        else
        {
            // Failure: Record failed audit log, DO NOT modify package or inventory records!
            opLog.MarkFailed(carrierResponse.ErrorMessage ?? "Carrier rejected request.");
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Carrier label generation failed for Package '{Pkg}': {Error}. Package remains in '{Status}' status.",
                package.PackageNumber, carrierResponse.ErrorMessage, package.Status);

            return new GenerateShippingLabelResult
            {
                IsSuccess = false,
                PackageId = package.Id,
                PackageNumber = package.PackageNumber,
                CarrierCode = request.CarrierCode,
                ServiceLevel = request.ServiceLevel,
                PackageStatus = package.Status, // Strictly remains unchanged
                IsSimulated = true,
                ErrorMessage = carrierResponse.ErrorMessage,
                Message = "Failed to generate shipping label from carrier."
            };
        }
    }

    public async Task<VoidShippingLabelResult> VoidLabelAsync(
        VoidShippingLabelRequest request,
        CancellationToken cancellationToken = default)
    {
        var label = await _context.ShippingLabels
            .Include(l => l.Package)
            .FirstOrDefaultAsync(l => l.Id == request.LabelId, cancellationToken);

        if (label == null)
        {
            throw new EntityNotFoundException(nameof(ShippingLabel), request.LabelId);
        }

        if (label.Status == ShippingLabelStatus.Voided)
        {
            throw new InvalidDomainStateException($"Shipping label '{label.TrackingNumber}' is already voided.");
        }

        var package = label.Package;
        if (package.Status == PackageStatus.Loaded)
        {
            throw new InvalidDomainStateException(
                $"Cannot void shipping label '{label.TrackingNumber}' because package '{package.PackageNumber}' is already loaded.");
        }

        string idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"VOID-{label.Id}"
            : request.IdempotencyKey.Trim();

        // 1. Log pending void operation
        var opLog = new CarrierOperationLog
        {
            ClientId = package.ClientId,
            FacilityId = package.FacilityId,
            PackageId = package.Id,
            OperationType = "VoidLabel",
            CarrierCode = label.CarrierCode,
            ServiceLevel = label.ServiceLevel.ToString(),
            IdempotencyKey = idempotencyKey,
            Status = "Pending",
            RequestPayload = $"Void tracking '{label.TrackingNumber}', Reason: '{request.Reason}'",
            StartedAtUtc = DateTime.UtcNow
        };
        _context.CarrierOperationLogs.Add(opLog);
        await _context.SaveChangesAsync(cancellationToken);

        // 2. Call external carrier service OUTSIDE DB transaction
        var voidReq = new CarrierVoidRequest
        {
            CarrierCode = label.CarrierCode,
            TrackingNumber = label.TrackingNumber,
            Reason = request.Reason,
            IdempotencyKey = idempotencyKey,
            SimulateFailure = request.SimulateFailure
        };

        CarrierVoidResponse voidRes;
        try
        {
            voidRes = await _carrierService.VoidShipmentAsync(voidReq, cancellationToken);
        }
        catch (Exception ex)
        {
            voidRes = new CarrierVoidResponse
            {
                IsSuccess = false,
                TrackingNumber = label.TrackingNumber,
                ErrorMessage = $"Carrier void exception: {ex.Message}"
            };
        }

        // 3. Persist void result in DB
        if (voidRes.IsSuccess)
        {
            label.Void(request.Reason);
            package.ClearShippingLabel(); // Reverts package to Packed status
            opLog.MarkSuccess(JsonSerializer.Serialize(voidRes), label.TrackingNumber);

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully voided shipping label '{Tracking}' for Package '{Pkg}'",
                label.TrackingNumber, package.PackageNumber);

            return new VoidShippingLabelResult
            {
                IsSuccess = true,
                LabelId = label.Id,
                PackageId = package.Id,
                TrackingNumber = label.TrackingNumber,
                LabelStatus = label.Status,
                PackageStatus = package.Status,
                VoidConfirmationNumber = voidRes.VoidConfirmationNumber,
                Message = "Shipping label successfully voided."
            };
        }
        else
        {
            opLog.MarkFailed(voidRes.ErrorMessage ?? "Carrier rejected void request.");
            await _context.SaveChangesAsync(cancellationToken);

            return new VoidShippingLabelResult
            {
                IsSuccess = false,
                LabelId = label.Id,
                PackageId = package.Id,
                TrackingNumber = label.TrackingNumber,
                LabelStatus = label.Status,
                PackageStatus = package.Status,
                ErrorMessage = voidRes.ErrorMessage,
                Message = "Failed to void shipping label with carrier."
            };
        }
    }

    public async Task<ShippingLabelDto?> GetLabelAsync(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var label = await _context.ShippingLabels
            .Include(l => l.Package)
            .FirstOrDefaultAsync(l => l.Id == labelId, cancellationToken);

        if (label == null) return null;

        return new ShippingLabelDto
        {
            Id = label.Id,
            PackageId = label.PackageId,
            PackageNumber = label.Package.PackageNumber,
            TrackingNumber = label.TrackingNumber,
            CarrierCode = label.CarrierCode,
            ServiceLevel = label.ServiceLevel,
            LabelFormat = label.LabelFormat,
            LabelData = label.LabelData,
            RoutingCode = label.RoutingCode,
            Status = label.Status,
            GeneratedAtUtc = label.GeneratedAtUtc,
            PrintedAtUtc = label.PrintedAtUtc,
            VoidedAtUtc = label.VoidedAtUtc,
            VoidReason = label.VoidReason
        };
    }

    public async Task<CarrierTrackingResponse> TrackPackageAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        var package = await _context.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, cancellationToken);

        if (package == null)
        {
            throw new EntityNotFoundException(nameof(Package), packageId);
        }

        if (string.IsNullOrWhiteSpace(package.TrackingNumber) || string.IsNullOrWhiteSpace(package.CarrierCode))
        {
            throw new InvalidDomainStateException($"Package '{package.PackageNumber}' has no tracking number or carrier assigned.");
        }

        return await _carrierService.TrackShipmentAsync(package.TrackingNumber, package.CarrierCode, cancellationToken);
    }
}
