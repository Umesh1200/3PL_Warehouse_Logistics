namespace Warehouse3PL.Infrastructure.Caching;

public class CacheOptions
{
    public const string SectionName = "Redis";

    public string? ConnectionString { get; set; }
    public string InstanceName { get; set; } = "Warehouse3PL:";
    public int DefaultExpirationMinutes { get; set; } = 30;
    public bool EnableRedis { get; set; } = false;
}

