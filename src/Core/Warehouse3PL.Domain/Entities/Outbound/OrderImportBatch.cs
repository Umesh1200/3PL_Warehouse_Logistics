using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Bulk order ingestion audit aggregate.
/// Tracks asynchronous batch progress, row-level validation outcomes, and duplicate prevention.
/// Phase 4.1 Order Management.
/// </summary>
public class OrderImportBatch : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;

    public int TotalRows { get; set; } = 0;
    public int SuccessfulRows { get; set; } = 0;
    public int FailedRows { get; set; } = 0;
    public int DuplicateRows { get; set; } = 0;

    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Processing;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public string? ErrorDetailsJson { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public ICollection<OrderImportBatchRow> Rows { get; set; } = [];

    public OrderImportBatch()
    {
        Id = Guid.NewGuid();
    }

    public void Complete(int successfulRows, int failedRows, int duplicateRows, string? errorJson = null)
    {
        SuccessfulRows = successfulRows;
        FailedRows = failedRows;
        DuplicateRows = duplicateRows;
        ErrorDetailsJson = errorJson;
        CompletedAtUtc = DateTime.UtcNow;

        Status = (failedRows > 0 || duplicateRows > 0)
            ? (successfulRows > 0 ? ImportBatchStatus.CompletedWithErrors : ImportBatchStatus.Failed)
            : ImportBatchStatus.Completed;
    }
}

