namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an individual shipping document / label.
/// Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public enum ShippingLabelStatus
{
    Generated = 1,   // Label payload generated from carrier
    Printed = 2,     // Thermal label output to packing bench printer
    Voided = 3       // Cancelled / voided with carrier
}

