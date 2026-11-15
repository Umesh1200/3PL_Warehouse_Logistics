namespace Warehouse3PL.Domain.Exceptions;

public class EntityNotFoundException : DomainException
{
    public string EntityName { get; }
    public object Key { get; }

    public EntityNotFoundException(string entityName, object key)
        : base($"Entity '{entityName}' with identifier '{key}' was not found.", "ENTITY_NOT_FOUND")
    {
        EntityName = entityName;
        Key = key;
    }
}

