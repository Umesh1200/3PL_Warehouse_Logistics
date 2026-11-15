using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Row-level audit and validation log within an OrderImportBatch.
/// Phase 4.1 Order Management.
/// </summary>
public class OrderImportBatchRow : BaseEntity<Guid>
{
    public Guid OrderImportBatchId { get; set; }
    public int RowNumber { get; set; }

    public string? ExternalOrderReference { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public string? ErrorMessage { get; set; }

    public Guid? CreatedOutboundOrderId { get; set; }

    // Navigation
    public OrderImportBatch OrderImportBatch { get; set; } = null!;

    public OrderImportBatchRow()
    {
        Id = Guid.NewGuid();
    }
}

