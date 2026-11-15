namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Categorization of transit delivery exceptions reported by carriers.
/// Phase 4.5 Outbound Shipment Tracking &amp; Delivery Confirmation (Step 3B).
/// </summary>
public enum DeliveryExceptionType
{
    AddressNotFound = 1,           // Street address invalid, missing apartment/suite number
    RecipientUnavailable = 2,      // Business closed, recipient absent, adult signature needed
    CustomerRefused = 3,           // Consignee refused receipt of package
    WeatherOrNaturalDisaster = 4,  // Severe weather, natural disaster, road closures
    CarrierOperationalDelay = 5,   // Missed sort cutoff, equipment failure, airline delay
    PackageDamagedInTransit = 6,   // Package visibly crushed/damaged in transit
    LostInTransit = 7,             // No tracking movement past transit SLA
    AccessRestricted = 8,          // Security gate, dog on premises, locked condominium code required
    MissedDeliveryAppointment = 9, // Delivery appointment slot missed or rescheduled
    DocumentationMissing = 10,     // Customs clearance, hazmat, or legal compliance documents missing
    Other = 99                     // Unclassified or custom carrier exception
}
