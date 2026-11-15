using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Warehouse3PL.WebApi.Extensions;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        const string serviceName = "Warehouse3PL.WebApi";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                if (configuration.GetValue<bool>("OpenTelemetry:ConsoleExporter", false))
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (configuration.GetValue<bool>("OpenTelemetry:ConsoleExporter", false))
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}

