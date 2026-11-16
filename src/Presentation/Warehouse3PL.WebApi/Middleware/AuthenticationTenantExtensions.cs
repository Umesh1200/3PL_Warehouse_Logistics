namespace Warehouse3PL.WebApi.Middleware;

public static class AuthenticationTenantExtensions
{
    /// <summary>Authenticate first so tenant-header validation uses verified identity claims.</summary>
    public static IApplicationBuilder UseAuthenticatedTenantResolution(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
