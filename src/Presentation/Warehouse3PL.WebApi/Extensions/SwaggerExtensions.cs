using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Warehouse3PL.WebApi.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "3PL Warehouse, Logistics, Courier & Algorithm Engine API",
                Version = "v1",
                Description = "High-throughput Third-Party Logistics (3PL) platform integrating WMS, Courier Shipping, Fleet Dispatch, and Algorithm Engine (DB-SPEC-3PL-2026).",
                Contact = new OpenApiContact
                {
                    Name = "3PL Logistics Engineering Team",
                    Email = "dev@3pllogistics.com"
                }
            });

            // 1. JWT Bearer Definition
            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter JWT Bearer token format: Bearer {your_token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            };

            options.AddSecurityDefinition("Bearer", securityScheme);

            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer"),
                    new List<string>()
                }
            });

            // 2. Add X-Tenant-Id header operation filter for multi-tenancy testing
            options.OperationFilter<TenantHeaderOperationFilter>();
        });

        return services;
    }
}

public class TenantHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<IOpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Description = "Optional multi-tenancy client UUID (propagates tenant scope for Staff or client tenant validation)",
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uuid"
            }
        });
    }
}
