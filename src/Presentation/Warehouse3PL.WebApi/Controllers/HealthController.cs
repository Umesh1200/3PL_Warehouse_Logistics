using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Observability & Health")]
public class HealthController : BaseApiController
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public HealthController(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    /// <summary>
    /// System and component health check endpoint.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> CheckHealth(CancellationToken ct)
    {
        var databaseHealthy = false;
        string? dbError = null;

        try
        {
            databaseHealthy = await _context.Clients.AnyAsync(ct);
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }

        var cacheHealthy = false;
        string? cacheError = null;

        try
        {
            await _cache.SetAsync("health-probe", "ok", slidingExpiration: TimeSpan.FromMinutes(1), cancellationToken: ct);
            var val = await _cache.GetAsync<string>("health-probe", ct);
            cacheHealthy = val == "ok";
        }
        catch (Exception ex)
        {
            cacheError = ex.Message;
        }

        var process = Process.GetCurrentProcess();

        return Ok(new
        {
            status = databaseHealthy ? "Healthy" : "Degraded",
            timestamp = DateTime.UtcNow,
            version = "1.0.0-phase1",
            components = new
            {
                database = new { status = databaseHealthy ? "UP" : "DOWN", error = dbError },
                cache = new { status = cacheHealthy ? "UP" : "DEGRADED", error = cacheError }
            },
            system = new
            {
                uptimeSeconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds,
                memoryAllocatedMb = process.WorkingSet64 / (1024 * 1024)
            }
        });
    }
}

