namespace Warehouse3PL.Domain.Constants;

public static class SystemRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string OperationsManager = "OperationsManager";
    public const string ClientAdmin = "ClientAdmin";
    public const string ClientUser = "ClientUser";
    public const string WarehouseOperator = "WarehouseOperator";
    public const string Driver = "Driver";

    public static IReadOnlyList<string> GetAllRoles() =>
    [
        SuperAdmin,
        OperationsManager,
        ClientAdmin,
        ClientUser,
        WarehouseOperator,
        Driver
    ];
}

