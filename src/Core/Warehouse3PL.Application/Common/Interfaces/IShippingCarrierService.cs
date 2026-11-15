using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class CarrierAddress
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string StreetLine1 { get; set; } = string.Empty;
    public string? StreetLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string StateOrProvince { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
    public string? Phone { get; set; }
}

public class CarrierParcel
{
    public string PackageNumber { get; set; } = string.Empty;
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }
    public decimal? DeclaredValue { get; set; }
}

public class CarrierRateRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public CarrierAddress OriginAddress { get; set; } = new();
    public CarrierAddress DestinationAddress { get; set; } = new();
    public CarrierParcel Parcel { get; set; } = new();
    public string? PreferredCarrierCode { get; set; }
    public CarrierServiceLevel? PreferredServiceLevel { get; set; }
}

public class CarrierRateQuote
{
    public string CarrierCode { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; }
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public int EstimatedDeliveryDays { get; set; }
    public bool IsGuaranteed { get; set; }
}

public class CarrierShipmentRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public CarrierAddress OriginAddress { get; set; } = new();
    public CarrierAddress DestinationAddress { get; set; } = new();
    public CarrierParcel Parcel { get; set; } = new();
    public string LabelFormat { get; set; } = "ZPL"; // ZPL or PDF
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool SimulateFailure { get; set; } = false;
}

public class CarrierShipmentResponse
{
    public bool IsSuccess { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel ServiceLevel { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string LabelFormat { get; set; } = "ZPL";
    public string LabelData { get; set; } = string.Empty;
    public string? RoutingCode { get; set; }
    public decimal RateCharged { get; set; }
    public string Currency { get; set; } = "USD";
    public string CarrierShipmentId { get; set; } = string.Empty;
    public bool IsSimulated { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class CarrierVoidRequest
{
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool SimulateFailure { get; set; } = false;
}

public class CarrierVoidResponse
{
    public bool IsSuccess { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string VoidConfirmationNumber { get; set; } = string.Empty;
    public bool IsSimulated { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class CarrierTrackingEvent
{
    public DateTime TimestampUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CarrierTrackingResponse
{
    public bool IsSuccess { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime? EstimatedDeliveryUtc { get; set; }
    public List<CarrierTrackingEvent> Events { get; set; } = [];
    public bool IsSimulated { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public interface IShippingCarrierService
{
    string CarrierIdentifier { get; }

    Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(
        CarrierRateRequest request,
        CancellationToken cancellationToken = default);

    Task<CarrierShipmentResponse> CreateShipmentAsync(
        CarrierShipmentRequest request,
        CancellationToken cancellationToken = default);

    Task<CarrierVoidResponse> VoidShipmentAsync(
        CarrierVoidRequest request,
        CancellationToken cancellationToken = default);

    Task<CarrierTrackingResponse> TrackShipmentAsync(
        string trackingNumber,
        string carrierCode,
        CancellationToken cancellationToken = default);
}

