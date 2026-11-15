namespace Warehouse3PL.Domain.Enums;

public enum StorageTempClass
{
    Ambient = 1,
    TemperatureControlled = 2, // 15°C to 25°C
    Refrigerated = 3,          // 2°C to 8°C
    Frozen = 4                 // -18°C and below
}

