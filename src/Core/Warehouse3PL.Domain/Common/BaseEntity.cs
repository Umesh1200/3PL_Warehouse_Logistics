namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Base entity with strongly-typed identifier.
/// </summary>
public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;

    private readonly List<DomainEvent> _domainEvents = [];

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void RemoveDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Remove(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

public abstract class BaseEntity : BaseEntity<Guid>
{
    protected BaseEntity()
    {
        Id = Guid.NewGuid();
    }
}

