namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Enforces multi-tenancy anchor (client_id). Entities implementing this interface
/// are partitioned by client tenant and subject to automatic global query filtering.
/// </summary>
public interface ITenantEntity
{
    Guid ClientId { get; set; }
}

