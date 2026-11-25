using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class TimeZoneOffsetResolver : ITimeZoneOffsetResolver
{
    public int GetUtcOffsetMinutes(string timeZoneId, DateTime utcInstant)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("TimeZoneId cannot be empty.", nameof(timeZoneId));
        }

        var utc = utcInstant.Kind switch
        {
            DateTimeKind.Utc => utcInstant,
            DateTimeKind.Local => utcInstant.ToUniversalTime(),
            _ => throw new ArgumentException(
                "Evaluation timestamp must specify DateTimeKind.Utc or DateTimeKind.Local.",
                nameof(utcInstant))
        };

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        return (int)tz.GetUtcOffset(utc).TotalMinutes;
    }

    public bool TryValidateTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
