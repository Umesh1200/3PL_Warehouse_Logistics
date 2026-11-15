using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Operational packing bench session.
/// Coordinates order packing at a designated PackingStation location and prevents concurrent conflicting packing attempts.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class PackingSession : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid PackingStationLocationId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid OperatorUserId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public PackingSessionStatus Status { get; set; } = PackingSessionStatus.Active;
    public string? Notes { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Location PackingStationLocation { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public User OperatorUser { get; set; } = null!;

    public PackingSession()
    {
        Id = Guid.NewGuid();
    }

    public void Complete(Guid userId)
    {
        if (Status != PackingSessionStatus.Active && Status != PackingSessionStatus.Suspended)
        {
            throw new InvalidDomainStateException(
                $"Cannot complete packing session '{Id}' with status '{Status}'. Session must be Active or Suspended.");
        }

        Status = PackingSessionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Suspend(string reason, Guid userId)
    {
        if (Status != PackingSessionStatus.Active)
        {
            throw new InvalidDomainStateException(
                $"Cannot suspend packing session '{Id}' with status '{Status}'. Session must be Active.");
        }

        Status = PackingSessionStatus.Suspended;
        Notes = reason;
    }

    public void Resume(Guid userId)
    {
        if (Status != PackingSessionStatus.Suspended)
        {
            throw new InvalidDomainStateException(
                $"Cannot resume packing session '{Id}' with status '{Status}'. Session must be Suspended.");
        }

        Status = PackingSessionStatus.Active;
    }

    public void Abort(string reason, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when aborting a packing session.");
        }

        if (Status == PackingSessionStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot abort completed packing session '{Id}'.");
        }

        Status = PackingSessionStatus.Aborted;
        Notes = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}

