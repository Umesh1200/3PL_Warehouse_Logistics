using Serilog.Core;
using Serilog.Events;
using Microsoft.AspNetCore.Http;

namespace Warehouse3PL.Infrastructure.Logging;

public class TenantLogEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantLogEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? httpContext.User.FindFirst("tenant_id")?.Value
            ?? "NoTenant";

        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? "Anonymous";

        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TenantId", tenantId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", userId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
    }
}

