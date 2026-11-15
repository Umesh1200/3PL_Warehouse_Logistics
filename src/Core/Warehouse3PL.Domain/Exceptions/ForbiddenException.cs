namespace Warehouse3PL.Domain.Exceptions;

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "You do not have permission to perform this action or access this tenant resource.")
        : base(message, "FORBIDDEN")
    {
    }
}

