using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Dock;

/// <summary>
/// Physical dock door belonging to a warehouse facility.
/// DB-SPEC-3PL-2026 Module 9 (dock_doors).
/// </summary>
public class DockDoor : AuditableEntity<Guid>, ISoftDeletable
{
    public const decimal CmPerFoot = 30.48m;

    public Guid FacilityId { get; set; }
    public Guid? LocationId { get; set; }
    public string DoorNumber { get; set; } = string.Empty;
    public string NormalizedDoorNumber { get; set; } = string.Empty;
    public DockDoorType DoorType { get; set; } = DockDoorType.Combination;
    public DockDoorStatus Status { get; set; } = DockDoorStatus.Active;
    
    // Physical capabilities & operational constraints (MaxTrailerLengthCm is authoritative canonical metric)
    public bool SupportsRefrigerated { get; set; } = false;
    public decimal? MaxTrailerLengthCm { get; set; }
    public int? MaxTrailerLengthFeet { get; set; }
    public string? Notes { get; set; }
    public long ConfigRevision { get; set; } = 1;

    public bool IsOperational => Status == DockDoorStatus.Active;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public Location? PhysicalLocation { get; set; }
    public ICollection<DockAppointment> Appointments { get; set; } = [];
    public ICollection<DockAssignment> Assignments { get; set; } = [];

    public DockDoor()
    {
        Id = Guid.NewGuid();
    }

    public static string NormalizeDoorNo(string doorNumber) =>
        (doorNumber ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Sets canonical <see cref="MaxTrailerLengthCm"/> and deterministically derives legacy <see cref="MaxTrailerLengthFeet"/>.
    /// Rejects conflicting non-null values when both units are supplied.
    /// </summary>
    public void SetMaxTrailerLength(decimal? maxTrailerLengthCm, int? maxTrailerLengthFeet)
    {
        if (maxTrailerLengthCm.HasValue && maxTrailerLengthCm.Value <= 0m)
        {
            throw new Warehouse3PL.Domain.Exceptions.ValidationException(nameof(maxTrailerLengthCm), "MaxTrailerLengthCm must be positive.");
        }

        if (maxTrailerLengthFeet.HasValue && maxTrailerLengthFeet.Value <= 0)
        {
            throw new Warehouse3PL.Domain.Exceptions.ValidationException(nameof(maxTrailerLengthFeet), "MaxTrailerLengthFeet must be positive.");
        }

        if (maxTrailerLengthCm.HasValue && maxTrailerLengthFeet.HasValue)
        {
            var expectedCm = Math.Round(maxTrailerLengthFeet.Value * CmPerFoot, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(maxTrailerLengthCm.Value - expectedCm) > 15.24m) // > 0.5 ft discrepancy
            {
                throw new Warehouse3PL.Domain.Exceptions.ValidationException(
                    nameof(maxTrailerLengthCm),
                    $"Conflicting trailer length values: {maxTrailerLengthCm.Value} cm does not match {maxTrailerLengthFeet.Value} ft ({expectedCm} cm).");
            }

            MaxTrailerLengthCm = Math.Round(maxTrailerLengthCm.Value, 2, MidpointRounding.AwayFromZero);
            MaxTrailerLengthFeet = (int)Math.Round(MaxTrailerLengthCm.Value / CmPerFoot, 0, MidpointRounding.AwayFromZero);
            return;
        }

        if (maxTrailerLengthCm.HasValue)
        {
            MaxTrailerLengthCm = Math.Round(maxTrailerLengthCm.Value, 2, MidpointRounding.AwayFromZero);
            MaxTrailerLengthFeet = (int)Math.Round(MaxTrailerLengthCm.Value / CmPerFoot, 0, MidpointRounding.AwayFromZero);
        }
        else if (maxTrailerLengthFeet.HasValue)
        {
            MaxTrailerLengthFeet = maxTrailerLengthFeet.Value;
            MaxTrailerLengthCm = Math.Round(maxTrailerLengthFeet.Value * CmPerFoot, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            MaxTrailerLengthCm = null;
            MaxTrailerLengthFeet = null;
        }
    }
}

