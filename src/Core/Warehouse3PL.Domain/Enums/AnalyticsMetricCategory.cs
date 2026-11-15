namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational and financial categories for 3PL analytics KPIs and Control Tower widgets.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public enum AnalyticsMetricCategory
{
    /// <summary>
    /// Discrete fulfillment execution (Orders received, allocated, picked, packed, dispatched).
    /// </summary>
    FulfillmentThroughput = 1,

    /// <summary>
    /// Worker and team operational velocity (picks/hr, lines/hr, cartons/hr, putaways/hr).
    /// </summary>
    LaborProductivity = 2,

    /// <summary>
    /// Warehouse volumetric and slot occupancy (% cube, pallet slots, aging dead stock).
    /// </summary>
    InventoryUtilization = 3,

    /// <summary>
    /// Courier transportation SLAs (On-time delivery %, first-attempt success %, exception rates).
    /// </summary>
    CarrierPerformance = 4,

    /// <summary>
    /// Client P&amp;L and commercial realization (Revenue, cost-to-serve, freight markup, AR aging).
    /// </summary>
    FinancialProfitability = 5,

    /// <summary>
    /// Return-to-sender and RMA processing (Return receipts, inspection yields, restocking velocity).
    /// </summary>
    ReverseLogistics = 6,

    /// <summary>
    /// Dock door throughput, yard truck turn times, and appointment schedule adherence.
    /// </summary>
    DockAndYard = 7
}

