using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Movable handling unit (LPN container) such as a reusable plastic tote, pick cart slot, or pallet.
/// Tracks current physical location (aisle, cart, packing station) and wave/order associations.
/// DB-SPEC-3PL-2026 Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public class HandlingUnit : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Unique License Plate Number / Barcode scanned by RF operators.
    /// </summary>
    public string LpnCode { get; set; } = string.Empty;

    public HandlingUnitType UnitType { get; set; } = HandlingUnitType.Tote;
    public HandlingUnitStatus Status { get; set; } = HandlingUnitStatus.Available;

    /// <summary>
    /// Current physical warehouse location (e.g. storage aisle during picking, or packing station PK-01).
    /// </summary>
    public Guid? CurrentLocationId { get; set; }

    public Guid? CurrentPickWaveId { get; set; }
    public Guid? CurrentOutboundOrderId { get; set; }

    public decimal? MaxWeightKg { get; set; }
    public decimal? MaxVolumeCm3 { get; set; }

    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Location? CurrentLocation { get; set; }
    public PickWave? CurrentPickWave { get; set; }
    public OutboundOrder? CurrentOutboundOrder { get; set; }
    public Package? Package { get; set; }

    public HandlingUnit()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Assigns the handling unit to an active pick wave and optional specific order.
    /// </summary>
    public void AssignToWave(Guid pickWaveId, Guid? orderId = null)
    {
        if (Status != HandlingUnitStatus.Available && Status != HandlingUnitStatus.InUse)
        {
            throw new InvalidDomainStateException(
                $"Cannot assign handling unit '{LpnCode}' with status '{Status}' to wave. Unit must be Available.");
        }

        CurrentPickWaveId = pickWaveId;
        CurrentOutboundOrderId = orderId;
        Status = HandlingUnitStatus.InUse;
    }

    /// <summary>
    /// Updates the physical location of the handling unit (e.g. arriving at packing station).
    /// </summary>
    public void MoveToLocation(Guid destinationLocationId, HandlingUnitStatus newStatus = HandlingUnitStatus.InTransit)
    {
        CurrentLocationId = destinationLocationId;
        Status = newStatus;
    }

    /// <summary>
    /// Releases the handling unit after packing/shipping, returning it to the clean available pool.
    /// </summary>
    public void Release()
    {
        CurrentPickWaveId = null;
        CurrentOutboundOrderId = null;
        Status = HandlingUnitStatus.Available;
    }
}

