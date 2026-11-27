using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class CreateReturnShipmentRequest
{
    public Guid AuthorizationId { get; set; }
    public string? CarrierCode { get; set; }
    public CarrierServiceLevel? ServiceLevel { get; set; }
    public string LabelFormat { get; set; } = "ZPL";
    public CarrierAddress? OriginAddressOverride { get; set; }
    public CarrierAddress? DestinationAddressOverride { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TenantIdOverride { get; set; }
    public bool SimulateCarrierFailure { get; set; } = false;
}

public class ReturnShipmentResult
{
    public bool IsSuccess { get; set; }
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public string AuthorizationNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    public string LabelFormat { get; set; } = "ZPL";
    public string? LabelData { get; set; }
    public string? LabelObjectKey { get; set; }
    public decimal? RateCharged { get; set; }
    public string Currency { get; set; } = "USD";
    public ReturnShipmentStatus Status { get; set; }
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public bool IsDuplicate { get; set; }
    public bool? CarrierCancellationConfirmed { get; set; }
    public string? CarrierCancellationNotes { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service managing the creation of reverse Return-To-Sender (RTS) shipments,
/// carrier booking, reverse tracking number generation, and carrier shipping label storage.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public interface IReturnShipmentService
{
    /// <summary>
    /// Creates a reverse ReturnShipment for an active ReturnAuthorization, books the carrier,
    /// acquires a valid reverse tracking number, generates the return label, and transitions
    /// the authorization to ShipmentCreated.
    /// Operates idempotently and keeps carrier API interaction outside DB transactions.
    /// </summary>
    Task<ReturnShipmentResult> CreateReturnShipmentAsync(
        CreateReturnShipmentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a return shipment by its identifier including navigations.
    /// </summary>
    Task<ReturnShipment?> GetReturnShipmentByIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a return shipment by its carrier tracking number.
    /// </summary>
    Task<ReturnShipment?> GetReturnShipmentByTrackingNumberAsync(
        string trackingNumber,
        string carrierCode,
        CancellationToken cancellationToken = default);
}

