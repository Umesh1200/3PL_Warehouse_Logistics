using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

