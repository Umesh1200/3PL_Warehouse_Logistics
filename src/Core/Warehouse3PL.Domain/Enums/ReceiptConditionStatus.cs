namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Physical condition of received inventory line upon barcode / LPN scan.
/// </summary>
public enum ReceiptConditionStatus
{
    Sound = 1,
    Damaged = 2,
    Defective = 3
}

