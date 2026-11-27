using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Infrastructure;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Extensions;
using Warehouse3PL.WebApi.Middleware;

// Supported executable database migration entry point (enforces MDM-1.6 preflight & maintenance lock):
//   dotnet run --project src/Presentation/Warehouse3PL.WebApi -- --migrate-db [--target <MigrationId>] [--seed-catalogs] [--connection <ConnStr>]
if (args.Any(a => string.Equals(a, "--migrate-db", StringComparison.OrdinalIgnoreCase)))
{
    string? targetMigration = null;
    string? cliConnStr = null;
    bool seedCatalogs = args.Any(a => string.Equals(a, "--seed-catalogs", StringComparison.OrdinalIgnoreCase));
    var hostArgs = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--migrate-db", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[i], "--seed-catalogs", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }
        if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            targetMigration = args[++i];
            continue;
        }
        if (string.Equals(args[i], "--connection", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            cliConnStr = args[++i];
            continue;
        }
        hostArgs.Add(args[i]);
    }

    var migBuilder = Host.CreateApplicationBuilder(hostArgs.ToArray());
    var connStr = cliConnStr
        ?? migBuilder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=warehouse3pl_db;Username=postgres;Password=postgres";

    migBuilder.Services.AddHttpContextAccessor();
    migBuilder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
    migBuilder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
    migBuilder.Services.AddScoped<ITenantProvider, TenantProvider>();
    migBuilder.Services.AddScoped<AuditableEntityInterceptor>();
    migBuilder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseNpgsql(connStr, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
        }).UseSnakeCaseNamingConvention();
    });

    using var migHost = migBuilder.Build();
    try
    {
        using var scope = migHost.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Mdm16UpgradePreflightService.ExecuteSafeUpgradeAsync(db, targetMigration, seedCatalogs);
        Console.WriteLine("Database migration completed cleanly via supported --migrate-db entry point.");
        return;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Database migration via --migrate-db failed: {ex.Message}");
        Environment.ExitCode = 1;
        return;
    }
}

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
