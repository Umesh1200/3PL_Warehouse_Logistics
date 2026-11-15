using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Domain.Constants;

namespace Warehouse3PL.Infrastructure.BackgroundJobs;

public static class HangfireExtensions
{
    public static IServiceCollection AddWarehouseHangfire(this IServiceCollection services, IConfiguration configuration)
    {
        var enableHangfire = configuration.GetValue<bool>("Hangfire:Enabled", false);
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (enableHangfire && !string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString));
            });

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Environment.ProcessorCount * 2;
                options.Queues = ["critical", "default", "algorithms", "reports"];
            });
        }

        return services;
    }

    public static IApplicationBuilder UseWarehouseHangfireDashboard(this IApplicationBuilder app, IConfiguration configuration)
    {
        var enableHangfire = configuration.GetValue<bool>("Hangfire:Enabled", false);
        if (!enableHangfire) return app;

        app.UseHangfireDashboard("/jobs", new DashboardOptions
        {
            DashboardTitle = "3PL Logistics Engine - Background Processing",
            Authorization = [new HangfireDashboardAuthorizationFilter()]
        });

        return app;
    }
}

public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow authenticated SuperAdmins or local development
        if (httpContext.Request.Host.Host == "localhost" || httpContext.Request.Host.Host == "127.0.0.1")
        {
            return true;
        }

        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole(SystemRoles.SuperAdmin);
    }
}

