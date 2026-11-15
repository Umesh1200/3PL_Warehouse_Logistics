namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Material handling equipment types utilized during warehouse labor task execution.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public enum WarehouseEquipmentType
{
    ManualCart = 1,
    Forklift = 2,
    ReachTruck = 3,
    OrderPicker = 4,
    PalletJack = 5,
    Conveyor = 6
}

