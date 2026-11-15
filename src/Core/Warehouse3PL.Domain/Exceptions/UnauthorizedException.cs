namespace Warehouse3PL.Domain.Exceptions;

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Authentication failed or token is invalid/expired.")
        : base(message, "UNAUTHORIZED")
    {
    }
}

