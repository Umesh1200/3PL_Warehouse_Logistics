namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Contractual billing policy defining how carrier accessorial charges are treated for client invoicing.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public enum AccessorialBillableTreatment
{
    /// <summary>
    /// Surcharge is marked up according to client freight markup rules.
    /// </summary>
    BillableWithMarkup = 1,

    /// <summary>
    /// Surcharge is passed through to the client at exact carrier cost (1:1).
    /// </summary>
    PassThroughAtCost = 2,

    /// <summary>
    /// Surcharge is absorbed by the 3PL warehouse (client charge is 0.00).
    /// </summary>
    AbsorbedByWarehouse = 3,

    /// <summary>
    /// Client is billed a fixed contractual fee regardless of carrier cost.
    /// </summary>
    FixedClientFee = 4
}

