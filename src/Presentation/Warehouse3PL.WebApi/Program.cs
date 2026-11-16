using System.Text.Json.Serialization;
using Serilog;
using Warehouse3PL.Application;
using Warehouse3PL.Infrastructure;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.WebApi.Extensions;
using Warehouse3PL.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Structured Logging
builder.ConfigureSerilog();

// 2. Application & Infrastructure Layers
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3. API Controllers & JSON Formatting
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 4. Swagger / OpenAPI Documentation
builder.Services.AddSwaggerDocumentation();

// 5. OpenTelemetry Observability
builder.Services.AddObservability(builder.Configuration);

// 6. CORS for Next.js App Router & Client Frontends
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// 7. Automatic Database Seeding on Startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        await dbInitializer.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Database initialization/seeding could not connect to PostgreSQL. Ensure PostgreSQL service is started.");
    }
}

// 8. HTTP Pipeline & Middleware Ordering
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging();

app.UseCors("AllowFrontend");

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "3PL Warehouse Logistics Engine v1");
    options.RoutePrefix = string.Empty; // Serve Swagger at application root '/'
    options.DisplayRequestDuration();
});

app.UseRouting();

app.UseAuthenticatedTenantResolution();
app.UseAuthorization();

app.UseWarehouseHangfireDashboard(app.Configuration);

app.MapControllers();

try
{
    Log.Information("Starting 3PL Warehouse & Logistics Engine API (Phase 1)...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
