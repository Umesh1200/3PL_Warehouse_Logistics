using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Database-enforced scope versioning entity for ABC inventory classification runs.
/// Guarantees cross-instance concurrency safety and atomic sequential version allocation
/// across distributed application instances.
/// DB-SPEC-3PL-2026 Phase 6.3.5 ABC Inventory Classification Engine.
/// </summary>
public class AbcClassificationScopeVersion : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public AbcClassificationCriteria Criteria { get; set; }
    public VelocityObservationWindow ObservationWindow { get; set; }
    public Guid? PolicyId { get; set; }

    /// <summary>
    /// Highest allocated classification version for this portfolio scope.
    /// Acts as an optimistic concurrency token in the database.
    /// </summary>
    public int LatestVersion { get; set; }

    /// <summary>
    /// Unique run ID associated with the latest version allocation.
    /// </summary>
    public Guid? LastRunId { get; set; }

    public DateTime LastAllocatedAtUtc { get; set; } = DateTime.UtcNow;

    public AbcClassificationScopeVersion()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Deterministically generates a deterministic scope GUID based on the scope partition key.
    /// Guarantees that concurrent inserts for the same scope attempt the same primary key,
    /// triggering database unique constraint enforcement across all providers.
    /// </summary>
    public static Guid GenerateScopeId(
        Guid facilityId,
        Guid? clientId,
        AbcClassificationCriteria criteria,
        VelocityObservationWindow observationWindow)
    {
        var key = $"{facilityId}_{clientId?.ToString() ?? "ALL"}_{(int)criteria}_{(int)observationWindow}";
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }

    public static AbcClassificationScopeVersion Create(
        Guid facilityId,
        Guid? clientId,
        AbcClassificationCriteria criteria,
        VelocityObservationWindow observationWindow,
        int initialVersion = 1,
        Guid? runId = null,
        Guid? policyId = null)
    {
        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        if (initialVersion < 1)
        {
            throw new ValidationException(nameof(initialVersion), "Initial version must be at least 1.");
        }

        return new AbcClassificationScopeVersion
        {
            Id = GenerateScopeId(facilityId, clientId, criteria, observationWindow),
            FacilityId = facilityId,
            ClientId = clientId,
            Criteria = criteria,
            ObservationWindow = observationWindow,
            LatestVersion = initialVersion,
            LastRunId = runId,
            PolicyId = policyId,
            LastAllocatedAtUtc = DateTime.UtcNow
        };
    }

    public void IncrementVersion(Guid? runId = null, Guid? policyId = null)
    {
        LatestVersion++;
        LastRunId = runId;
        if (policyId.HasValue)
        {
            PolicyId = policyId;
        }
        LastAllocatedAtUtc = DateTime.UtcNow;
    }
}
