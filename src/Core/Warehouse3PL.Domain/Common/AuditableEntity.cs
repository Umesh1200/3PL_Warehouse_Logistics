namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Entity with creation and update tracking metadata.
/// </summary>
public abstract class AuditableEntity<TKey> : BaseEntity<TKey>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public abstract class AuditableEntity : AuditableEntity<Guid>
{
    protected AuditableEntity()
    {
        Id = Guid.NewGuid();
    }
}

