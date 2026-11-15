using Microsoft.AspNetCore.Http;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Authentication;

public class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _explicitTenantId;

    public TenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            if (_explicitTenantId.HasValue)
            {
                return _explicitTenantId.Value;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            // 1. Check for header 'X-Tenant-Id'
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue) &&
                Guid.TryParse(headerValue, out var headerTenantId))
            {
                return headerTenantId;
            }

            // 2. Check for token claim 'tenant_id' or 'client_id'
            var claim = httpContext.User.FindFirst("tenant_id")?.Value
                ?? httpContext.User.FindFirst("client_id")?.Value;

            if (Guid.TryParse(claim, out var tokenTenantId))
            {
                return tokenTenantId;
            }

            return null;
        }
    }

    public bool HasTenant => TenantId.HasValue;

    public bool BypassTenantFilter { get; set; }

    public void SetTenantId(Guid tenantId)
    {
        _explicitTenantId = tenantId;
    }
}

