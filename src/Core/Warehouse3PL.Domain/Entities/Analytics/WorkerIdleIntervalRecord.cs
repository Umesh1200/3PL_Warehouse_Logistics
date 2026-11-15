using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Operational downtime and non-productive shift interval record categorized by reason code.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public class WorkerIdleIntervalRecord : AuditableEntity<Guid>
{
    public Guid WorkerUserId { get; set; }
    public Guid FacilityId { get; set; }
    public string Department { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }

    public decimal DurationMinutes { get; set; }
    public decimal DurationSeconds { get; set; }

    public LaborIdleTimeReason ReasonCode { get; set; }
    public bool SupervisorApproved { get; set; }
    public string? Notes { get; set; }

    public WorkerIdleIntervalRecord()
    {
        Id = Guid.NewGuid();
    }

    public static WorkerIdleIntervalRecord Create(
        Guid workerUserId,
        Guid facilityId,
        string department,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        LaborIdleTimeReason reasonCode,
        bool supervisorApproved = false,
        string? notes = null)
    {
        if (workerUserId == Guid.Empty || facilityId == Guid.Empty)
            throw new InvalidDomainStateException("WorkerUserId and FacilityId are required.");

        if (endedAtUtc < startedAtUtc)
            throw new InvalidDomainStateException("EndedAtUtc cannot be earlier than StartedAtUtc.");

        var totalSeconds = (decimal)(endedAtUtc - startedAtUtc).TotalSeconds;
        var seconds = Math.Max(0m, Math.Round(totalSeconds, 2, MidpointRounding.AwayFromZero));
        var minutes = Math.Max(0m, Math.Round(seconds / 60m, 2, MidpointRounding.AwayFromZero));

        return new WorkerIdleIntervalRecord
        {
            WorkerUserId = workerUserId,
            FacilityId = facilityId,
            Department = string.IsNullOrWhiteSpace(department) ? "General" : department.Trim(),
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = seconds,
            DurationMinutes = minutes,
            ReasonCode = reasonCode,
            SupervisorApproved = supervisorApproved,
            Notes = notes
        };
    }
}

