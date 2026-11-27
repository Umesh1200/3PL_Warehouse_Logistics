using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Application.Common.Behaviors;
using Warehouse3PL.Application.Common.Mappings;
using Warehouse3PL.Application.Common.Security;

namespace Warehouse3PL.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Register Mapster
        MapsterConfig.RegisterMappings();

        // Register FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Register Operational Authorization
        services.AddScoped<WarehouseOperationalAuthorization>();
        services.AddScoped<OutboundOperationalAuthorization>();
        services.AddScoped<CompliancePlatformAuthorization>();
        services.AddScoped<WarehouseMasterAuthorization>();

        // Register MediatR & Pipeline Behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        });

        return services;
    }
}

