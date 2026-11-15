using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Contractual storage billing policy defining how each inventory holding status is treated commercially.
/// Can be client-wide (FacilityId == null) or facility-specific.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public class StorageBillingPolicy : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public string PolicyName { get; set; } = "Standard 3PL Storage Policy";

    // Status Category Treatments
    public StorageBillingTreatment AvailableTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment AllocatedTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment PickedTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment DamagedTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment QuarantinedTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment OnHoldTreatment { get; set; } = StorageBillingTreatment.BillableStandard;
    public StorageBillingTreatment InTransitTreatment { get; set; } = StorageBillingTreatment.Excluded;
    public StorageBillingTreatment ReturnPendingInspectionTreatment { get; set; } = StorageBillingTreatment.Excluded;

    /// <summary>
    /// Default fallback CBM per unit if an item has zero/missing dimensions (ensures no free storage).
    /// </summary>
    public decimal FallbackUnitCbm { get; set; } = 0.0100m; // Default 10 liters / 0.01 m3

    /// <summary>
    /// Fallback conversion quantity if item has no packaging unit for Pallet (e.g. 1000 units/pallet).
    /// </summary>
    public decimal FallbackUnitsPerPallet { get; set; } = 1000m;

    /// <summary>
    /// Fallback conversion quantity if item has no packaging unit for Carton (e.g. 20 units/carton).
    /// </summary>
    public decimal FallbackUnitsPerCarton { get; set; } = 20m;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility? Facility { get; set; }

    public StorageBillingPolicy()
    {
        Id = Guid.NewGuid();
    }

    public static StorageBillingPolicy CreateDefault(Guid clientId, Guid? facilityId = null, string? policyName = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for storage billing policy.");
        }

        return new StorageBillingPolicy
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            PolicyName = policyName ?? (facilityId.HasValue ? "Facility Override Storage Policy" : "Standard Client-Wide Storage Policy"),
            AvailableTreatment = StorageBillingTreatment.BillableStandard,
            AllocatedTreatment = StorageBillingTreatment.BillableStandard,
            PickedTreatment = StorageBillingTreatment.BillableStandard,
            DamagedTreatment = StorageBillingTreatment.BillableStandard,
            QuarantinedTreatment = StorageBillingTreatment.BillableStandard,
            OnHoldTreatment = StorageBillingTreatment.BillableStandard,
            InTransitTreatment = StorageBillingTreatment.Excluded,
            ReturnPendingInspectionTreatment = StorageBillingTreatment.Excluded,
            FallbackUnitCbm = 0.0100m,
            FallbackUnitsPerPallet = 1000m,
            FallbackUnitsPerCarton = 20m
        };
    }

    /// <summary>
    /// Determines whether a given inventory holding status is commercially billable under this policy.
    /// </summary>
    public StorageBillingTreatment GetTreatment(InventoryHoldingStatus status)
    {
        return status switch
        {
            InventoryHoldingStatus.Available => AvailableTreatment,
            InventoryHoldingStatus.Allocated => AllocatedTreatment,
            InventoryHoldingStatus.Picked => PickedTreatment,
            InventoryHoldingStatus.Damaged => DamagedTreatment,
            InventoryHoldingStatus.Quarantined => QuarantinedTreatment,
            InventoryHoldingStatus.OnHold => OnHoldTreatment,
            InventoryHoldingStatus.InTransit => InTransitTreatment,
            InventoryHoldingStatus.ReturnPendingInspection => ReturnPendingInspectionTreatment,
            _ => StorageBillingTreatment.BillableStandard
        };
    }

    public bool IsBillable(InventoryHoldingStatus status)
    {
        return GetTreatment(status) != StorageBillingTreatment.Excluded;
    }
}

