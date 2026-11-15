namespace Warehouse3PL.Domain.Exceptions;

/// <summary>
/// Thrown when an entity operation is attempted in an invalid state machine status.
/// </summary>
public class InvalidDomainStateException : DomainException
{
    public InvalidDomainStateException(string message) : base(message)
    {
    }
}

