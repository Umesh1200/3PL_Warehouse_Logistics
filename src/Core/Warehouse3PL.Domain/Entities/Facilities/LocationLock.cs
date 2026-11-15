using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Active lock or hold on a location (e.g. Quarantine, Cycle Count, Physical Damage, Maintenance).
/// </summary>
public class LocationLock : BaseEntity<Guid>
{
    public Guid LocationId { get; set; }
    public LocationLockType LockType { get; set; } = LocationLockType.Quarantine;
    public string ReasonCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? LockedByUserId { get; set; }
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; set; }
    public Guid? ReleasedByUserId { get; set; }

    public bool IsActive => !ReleasedAt.HasValue;

    // Navigations
    public Location Location { get; set; } = null!;
    public User? LockedByUser { get; set; }
    public User? ReleasedByUser { get; set; }

    public LocationLock()
    {
        Id = Guid.NewGuid();
    }
}

