namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an asynchronous or bulk order import batch.
/// Phase 4.1 Order Management.
/// </summary>
public enum ImportBatchStatus
{
    Processing = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4
}

