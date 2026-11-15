namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Categorization of commercial billable warehouse, courier, and logistics activities.
/// DB-SPEC-3PL-2026 Phase 5.0 Billing Foundation &amp; Activity-Based Costing.
/// </summary>
public enum BillableActivityType
{
    // Inbound Operations (Phase 3)
    InboundReceiving = 1,
    InboundInspection = 2,
    Putaway = 3,
    CrossDock = 4,

    // Outbound Operations (Phase 4)
    OrderProcessing = 10,
    PickLine = 11,
    PickUnit = 12,
    PackingCarton = 13,
    PackagingMaterial = 14,
    ShippingHandling = 15,
    CarrierFreight = 16,

    // Reverse Logistics Operations (Phase 4.5)
    ReturnProcessing = 20,
    ReturnInspection = 21,
    Restocking = 22,

    // Storage & Inventory Holding (Daily Snapshots)
    StoragePalletDay = 30,
    StorageCartonDay = 31,
    StorageCbmDay = 32,

    // Value-Added Services (VAS)
    SpecialHandling = 40,
    KittingAndAssembly = 41,
    CustomVas = 99
}

