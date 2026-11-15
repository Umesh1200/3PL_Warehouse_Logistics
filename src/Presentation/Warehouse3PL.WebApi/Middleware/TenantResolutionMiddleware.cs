using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.WebApi.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        // 1. Check if 'X-Tenant-Id' header is provided
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) &&
            Guid.TryParse(tenantHeader, out var requestedTenantId))
        {
            // If user is authenticated and is NOT staff, ensure they cannot spoof a different tenant
            if (currentUser.IsAuthenticated && !currentUser.IsStaff && currentUser.TenantId.HasValue)
            {
                if (currentUser.TenantId.Value != requestedTenantId)
                {
                    _logger.LogWarning(
                        "Tenant breach attempt: User {UserId} (Tenant {UserTenant}) attempted to access Tenant {TargetTenant}",
                        currentUser.UserId, currentUser.TenantId, requestedTenantId);

                    throw new ForbiddenException("Cross-tenant access forbidden. You cannot access another client's partition.");
                }
            }

            tenantProvider.SetTenantId(requestedTenantId);
        }

        await _next(context);
    }
}

