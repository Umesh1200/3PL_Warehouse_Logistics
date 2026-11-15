namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Base class for domain events triggered within the domain model.
/// </summary>
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

