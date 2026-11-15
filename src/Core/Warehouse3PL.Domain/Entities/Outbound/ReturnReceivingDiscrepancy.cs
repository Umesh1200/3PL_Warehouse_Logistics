using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Discrepancy record documenting quantity shortages, overages, incorrect items,
/// or missing components identified during return physical verification.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public class ReturnReceivingDiscrepancy : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ReturnReceivingSessionId { get; set; }
    public Guid? ItemId { get; set; }

    public string? ExpectedSku { get; set; }
    public string? ActualSku { get; set; }

    public ReturnDiscrepancyType DiscrepancyType { get; set; } = ReturnDiscrepancyType.Shortage;
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal DiscrepancyQuantity { get; set; }

    public string Notes { get; set; } = string.Empty;
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigations
    public ReturnReceivingSession ReturnReceivingSession { get; set; } = null!;
    public Item? Item { get; set; }

    public ReturnReceivingDiscrepancy()
    {
        Id = Guid.NewGuid();
    }

    public static ReturnReceivingDiscrepancy Create(
        Guid clientId,
        Guid sessionId,
        Guid? itemId,
        string? expectedSku,
        string? actualSku,
        ReturnDiscrepancyType type,
        decimal expectedQty,
        decimal receivedQty,
        string notes)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (sessionId == Guid.Empty)
        {
            throw new ValidationException(nameof(sessionId), "Session ID is required.");
        }

        var diff = Math.Abs(expectedQty - receivedQty);

        return new ReturnReceivingDiscrepancy
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ReturnReceivingSessionId = sessionId,
            ItemId = itemId,
            ExpectedSku = expectedSku?.Trim(),
            ActualSku = actualSku?.Trim(),
            DiscrepancyType = type,
            ExpectedQuantity = expectedQty,
            ReceivedQuantity = receivedQty,
            DiscrepancyQuantity = diff,
            Notes = notes.Trim(),
            ReportedAtUtc = DateTime.UtcNow
        };
    }
}

