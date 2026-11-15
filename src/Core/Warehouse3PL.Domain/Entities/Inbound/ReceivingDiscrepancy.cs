using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Discrepancy, damage capture, or cold chain temperature excursion recorded during inbound intake.
/// DB-SPEC-3PL-2026 Module 5.
/// </summary>
public class ReceivingDiscrepancy : BaseEntity<Guid>, ISoftDeletable
{
    public Guid ReceivingSessionId { get; set; }
    public Guid? ReceiptLineId { get; set; }

    public DiscrepancyType Type { get; set; }
    public decimal DiscrepantQuantity { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DiscrepancyResolutionStatus Status { get; set; } = DiscrepancyResolutionStatus.PendingReview;

    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on discrepancy resolutions.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public ReceivingSession ReceivingSession { get; set; } = null!;
    public ReceiptLine? ReceiptLine { get; set; }
    public User? ResolvedByUser { get; set; }

    public ReceivingDiscrepancy()
    {
        Id = Guid.NewGuid();
    }

    public void Resolve(DiscrepancyResolutionStatus resolution, Guid resolvedByUserId, string? notes = null)
    {
        if (Status != DiscrepancyResolutionStatus.PendingReview && Status != DiscrepancyResolutionStatus.Quarantined)
        {
            throw new DomainException($"Cannot resolve discrepancy '{Id}' because it is already '{Status}'.");
        }

        Status = resolution;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolutionNotes = notes ?? ResolutionNotes;
        Version++;
    }
}

