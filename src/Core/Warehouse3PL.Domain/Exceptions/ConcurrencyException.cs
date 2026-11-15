namespace Warehouse3PL.Domain.Exceptions;

public class ConcurrencyException : DomainException
{
    public ConcurrencyException(string message, string? code = "CONCURRENCY_CONFLICT") : base(message, code) { }
    public ConcurrencyException(string message, Exception innerException, string? code = "CONCURRENCY_CONFLICT") : base(message, innerException, code) { }
}
