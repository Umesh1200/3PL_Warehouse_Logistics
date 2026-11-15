using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Simulated multi-carrier shipping provider for development, automated testing, and offline operations.
/// Explicitly identified as simulated: produces deterministic rates, tracking numbers, and ZPL/PDF label data.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation (Step 3A).
/// </summary>
public class SimulatedCarrierService : IShippingCarrierService
{
    private readonly ILogger<SimulatedCarrierService> _logger;

    public string CarrierIdentifier => "SIMULATED";

    public SimulatedCarrierService(ILogger<SimulatedCarrierService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(
        CarrierRateRequest request,
        CancellationToken cancellationToken = default)
    {
        var quotes = new List<CarrierRateQuote>();
        var weight = Math.Max(0.5m, request.Parcel.WeightKg);

        var carriers = new[]
        {
            ("SIM-FEDEX", "FedEx Express (Simulated)", 12.50m, 1.80m),
            ("SIM-UPS", "UPS Ground (Simulated)", 11.00m, 1.65m),
            ("SIM-USPS", "USPS Priority (Simulated)", 8.50m, 1.40m),
            ("SIM-FREIGHT", "Freight LTL (Simulated)", 45.00m, 0.75m)
        };

        var serviceLevels = new[]
        {
            (CarrierServiceLevel.StandardGround, "Ground Delivery", 1.0m, 3, false),
            (CarrierServiceLevel.SecondDayAir, "2-Day Expedited Air", 1.8m, 2, true),
            (CarrierServiceLevel.NextDayAir, "Next Day Priority Air", 2.6m, 1, true),
            (CarrierServiceLevel.PriorityOvernight, "Overnight Early Morning", 3.4m, 1, true)
        };

        foreach (var (code, name, baseRate, weightRate) in carriers)
        {
            if (!string.IsNullOrWhiteSpace(request.PreferredCarrierCode) &&
                !string.Equals(request.PreferredCarrierCode, code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (level, desc, multiplier, days, guaranteed) in serviceLevels)
            {
                if (request.PreferredServiceLevel.HasValue && request.PreferredServiceLevel.Value != level)
                {
                    continue;
                }

                // Skip air services for Freight
                if (code == "SIM-FREIGHT" && level != CarrierServiceLevel.StandardGround && level != CarrierServiceLevel.FreightLTL)
                {
                    continue;
                }

                var calculatedCost = Math.Round((baseRate + (weight * weightRate)) * multiplier, 2);

                quotes.Add(new CarrierRateQuote
                {
                    CarrierCode = code,
                    CarrierName = name,
                    ServiceLevel = level,
                    ServiceDescription = $"{name} - {desc}",
                    TotalAmount = calculatedCost,
                    Currency = "USD",
                    EstimatedDeliveryDays = days,
                    IsGuaranteed = guaranteed
                });
            }
        }

        _logger.LogInformation(
            "Simulated carrier quoted {Count} rates for package '{Package}' (Weight: {Weight}kg)",
            quotes.Count, request.Parcel.PackageNumber, weight);

        return Task.FromResult<IReadOnlyList<CarrierRateQuote>>(quotes);
    }

    public Task<CarrierShipmentResponse> CreateShipmentAsync(
        CarrierShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Chaos / Failure testing injection
        if (request.SimulateFailure)
        {
            _logger.LogWarning("Simulated carrier forced failure requested for Package '{Pkg}'", request.PackageNumber);
            return Task.FromResult(new CarrierShipmentResponse
            {
                IsSuccess = false,
                CarrierCode = request.CarrierCode,
                ServiceLevel = request.ServiceLevel,
                IsSimulated = true,
                ErrorMessage = "SIMULATED_CARRIER_ERROR: Gateway timed out or address invalid (Simulated)."
            });
        }

        // Generate deterministic tracking number based on Carrier, PackageNumber, and Zip
        string seed = $"{request.CarrierCode}:{request.PackageNumber}:{request.DestinationAddress.PostalCode}";
        string trackingNumber = GenerateDeterministicTrackingNumber(request.CarrierCode, seed);
        string carrierShipmentId = $"SIM-SHP-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        string routingCode = $"SIM-HUB-{request.DestinationAddress.StateOrProvince.ToUpperInvariant()}-01";

        decimal weight = Math.Max(0.5m, request.Parcel.WeightKg);
        decimal rate = Math.Round(12.50m + (weight * 1.75m), 2);

        string labelData;
        string format = request.LabelFormat.ToUpperInvariant();
        if (format == "PDF")
        {
            labelData = GenerateSimulatedPdfLabel(request, trackingNumber, routingCode);
        }
        else
        {
            format = "ZPL";
            labelData = GenerateSimulatedZplLabel(request, trackingNumber, routingCode);
        }

        _logger.LogInformation(
            "Simulated carrier generated label for Package '{Pkg}', Tracking: '{Tracking}', Format: '{Format}'",
            request.PackageNumber, trackingNumber, format);

        return Task.FromResult(new CarrierShipmentResponse
        {
            IsSuccess = true,
            CarrierCode = request.CarrierCode,
            ServiceLevel = request.ServiceLevel,
            TrackingNumber = trackingNumber,
            LabelFormat = format,
            LabelData = labelData,
            RoutingCode = routingCode,
            RateCharged = rate,
            Currency = "USD",
            CarrierShipmentId = carrierShipmentId,
            IsSimulated = true
        });
    }

    public Task<CarrierVoidResponse> VoidShipmentAsync(
        CarrierVoidRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SimulateFailure)
        {
            _logger.LogWarning("Simulated carrier forced failure during void for Tracking '{Tracking}'", request.TrackingNumber);
            return Task.FromResult(new CarrierVoidResponse
            {
                IsSuccess = false,
                TrackingNumber = request.TrackingNumber,
                IsSimulated = true,
                ErrorMessage = "SIMULATED_CARRIER_ERROR: Unable to void manifest; carrier cut-off reached."
            });
        }

        string voidConfirm = $"VOID-CONF-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        _logger.LogInformation("Simulated carrier voided tracking '{Tracking}'. Confirm: '{Confirm}'", request.TrackingNumber, voidConfirm);

        return Task.FromResult(new CarrierVoidResponse
        {
            IsSuccess = true,
            TrackingNumber = request.TrackingNumber,
            VoidConfirmationNumber = voidConfirm,
            IsSimulated = true
        });
    }

    public Task<CarrierTrackingResponse> TrackShipmentAsync(
        string trackingNumber,
        string carrierCode,
        CancellationToken cancellationToken = default)
    {
        var response = new CarrierTrackingResponse
        {
            IsSuccess = true,
            TrackingNumber = trackingNumber,
            CarrierCode = carrierCode,
            CurrentStatus = "InTransit",
            EstimatedDeliveryUtc = DateTime.UtcNow.AddDays(2),
            IsSimulated = true,
            Events =
            [
                new CarrierTrackingEvent
                {
                    TimestampUtc = DateTime.UtcNow.AddHours(-12),
                    Status = "ManifestCreated",
                    Location = "Columbus, OH",
                    Description = "Electronic shipping data received (Simulated)"
                },
                new CarrierTrackingEvent
                {
                    TimestampUtc = DateTime.UtcNow.AddHours(-4),
                    Status = "DepartedFacility",
                    Location = "Columbus Sort Facility, OH",
                    Description = "In transit to destination facility (Simulated)"
                }
            ]
        };

        return Task.FromResult(response);
    }

    #region Label Generation Helpers

    private static string GenerateDeterministicTrackingNumber(string carrierCode, string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var hex = Convert.ToHexString(hash);
        var prefix = carrierCode.ToUpperInvariant().Replace("SIM-", "");
        return $"SIM-{prefix}-{hex[..12].ToUpperInvariant()}";
    }

    private static string GenerateSimulatedZplLabel(
        CarrierShipmentRequest request,
        string trackingNumber,
        string routingCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine("^PW812");
        sb.AppendLine("^LL1218");
        sb.AppendLine($"^FO50,50^ADN,36,20^FD{request.CarrierCode} - {request.ServiceLevel} (SIMULATED)^FS");
        sb.AppendLine($"^FO50,100^ADN,24,14^FDSHIP FROM: 3PL WAREHOUSE HUB^FS");
        sb.AppendLine($"^FO50,130^ADN,24,14^FDSHIP TO: {request.DestinationAddress.Name}^FS");
        sb.AppendLine($"^FO50,160^ADN,24,14^FD{request.DestinationAddress.StreetLine1}^FS");
        sb.AppendLine($"^FO50,190^ADN,24,14^FD{request.DestinationAddress.City}, {request.DestinationAddress.StateOrProvince} {request.DestinationAddress.PostalCode}^FS");
        sb.AppendLine($"^FO50,230^ADN,20,12^FDROUTING: {routingCode}^FS");
        sb.AppendLine($"^FO50,260^ADN,20,12^FDWEIGHT: {request.Parcel.WeightKg:F2} KG | PKG: {request.PackageNumber}^FS");
        sb.AppendLine("^FO50,300^BY3");
        sb.AppendLine($"^BCN,120,Y,N,N^FD{trackingNumber}^FS");
        sb.AppendLine($"^FO50,460^ADN,18,10^FDIDEMPOTENCY: {request.IdempotencyKey}^FS");
        sb.AppendLine("^XZ");
        return sb.ToString();
    }

    private static string GenerateSimulatedPdfLabel(
        CarrierShipmentRequest request,
        string trackingNumber,
        string routingCode)
    {
        string rawText = $"%PDF-1.4 [SIMULATED SHIPPING LABEL]\n" +
                         $"Carrier: {request.CarrierCode} ({request.ServiceLevel})\n" +
                         $"To: {request.DestinationAddress.Name}, {request.DestinationAddress.City}, {request.DestinationAddress.StateOrProvince} {request.DestinationAddress.PostalCode}\n" +
                         $"Package: {request.PackageNumber} | Weight: {request.Parcel.WeightKg:F2}kg\n" +
                         $"Tracking: {trackingNumber}\n" +
                         $"Routing: {routingCode}\n" +
                         $"Date: {DateTime.UtcNow:O}\n" +
                         $"%%EOF";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawText));
    }

    #endregion
}

