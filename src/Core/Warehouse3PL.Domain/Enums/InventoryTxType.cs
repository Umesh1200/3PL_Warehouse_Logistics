namespace Warehouse3PL.Domain.Enums;

public enum InventoryTxType
{
    Receive = 1,
    Putaway = 2,
    Pick = 3,
    Move = 4,
    AdjustPositive = 5,
    AdjustNegative = 6,
    CycleCount = 7,
    Scrap = 8,
    StatusChange = 9,
    Transfer = 10,
    Pack = 11
}
