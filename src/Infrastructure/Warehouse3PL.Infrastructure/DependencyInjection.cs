using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Warehouse3PL.Infrastructure.Caching;
using Warehouse3PL.Infrastructure.Configuration;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Shipping;

namespace Warehouse3PL.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Core Services & Contexts
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<IPutawayLocationSelector, PutawayLocationSelector>();
        services.AddScoped<IPutawayRecommendationService, PutawayLocationSelector>();
        services.AddScoped<ICrossDockEngine, CrossDockEngine>();
        services.AddScoped<IAllocationCandidateFilter, AllocationCandidateFilter>();
        services.AddScoped<IAllocationStrategy, FifoAllocationStrategy>();
        services.AddScoped<IAllocationStrategy, FefoAllocationStrategy>();
        services.AddScoped<IAllocationStrategy, ManualAllocationStrategy>();
        services.AddScoped<IAllocationStrategyResolver, AllocationStrategyResolver>();
        services.AddScoped<IAllocationEngine, AllocationEngine>();
        services.AddScoped<IAllocationExecutionService, AllocationExecutionService>();
        services.AddScoped<IPickRouteOptimizer, SerpentineRouteOptimizer>();
        services.AddScoped<IPickTaskGenerator, PickTaskGenerator>();
        services.AddScoped<IPickWaveGenerator, PickWaveGenerator>();
        services.AddScoped<IPickExecutionService, PickExecutionService>();
        services.AddScoped<ICartonizationService, CartonizationService>();
        services.AddScoped<IPackingExecutionService, PackingExecutionService>();
        services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
        services.AddScoped<IShippingLabelService, ShippingLabelService>();
        services.AddScoped<IShipmentConsolidationService, ShipmentConsolidationService>();
        services.AddScoped<IShipmentTrackingService, ShipmentTrackingService>();
        services.AddScoped<IProofOfDeliveryService, Warehouse3PL.Infrastructure.Services.Shipping.ProofOfDeliveryService>();
        services.AddScoped<IDeliveryConfirmationService, Warehouse3PL.Infrastructure.Services.Shipping.DeliveryConfirmationService>();
        services.AddScoped<ICarrierExceptionClassificationService, Warehouse3PL.Infrastructure.Services.Shipping.CarrierExceptionClassificationService>();
        services.AddScoped<IDeliveryExceptionService, Warehouse3PL.Infrastructure.Services.Shipping.DeliveryExceptionService>();
        services.AddScoped<IReturnAuthorizationService, Warehouse3PL.Infrastructure.Services.Shipping.ReturnAuthorizationService>();
        services.AddScoped<IReturnShipmentService, ReturnShipmentService>();
        services.AddScoped<IReturnTrackingService, ReturnTrackingService>();
        services.AddScoped<IReturnReceivingService, ReturnReceivingService>();

        // Commercial Billing & Costing Foundation (Phase 5.0, 5.1, 5.2, 5.3, 5.4 & 5.5)
        services.AddScoped<IBillableActivityService, Warehouse3PL.Infrastructure.Services.Billing.BillableActivityService>();
        services.AddScoped<IRateCardService, Warehouse3PL.Infrastructure.Services.Billing.RateCardService>();
        services.AddScoped<IRatingEngineService, Warehouse3PL.Infrastructure.Services.Billing.RatingEngineService>();
        services.AddScoped<IInventorySnapshotService, Warehouse3PL.Infrastructure.Services.Billing.InventorySnapshotService>();
        services.AddScoped<IStorageBillingService, Warehouse3PL.Infrastructure.Services.Billing.StorageBillingService>();
        services.AddScoped<IHandlingActivityIngestionService, Warehouse3PL.Infrastructure.Services.Billing.HandlingActivityIngestionService>();
        services.AddScoped<IFreightMarginEngineService, Warehouse3PL.Infrastructure.Services.Billing.FreightMarginEngineService>();
        services.AddScoped<IBillingRunService, Warehouse3PL.Infrastructure.Services.Billing.BillingRunService>();
        services.AddScoped<IInvoiceService, Warehouse3PL.Infrastructure.Services.Billing.InvoiceService>();
        services.AddScoped<IAccountsReceivableService, Warehouse3PL.Infrastructure.Services.Billing.AccountsReceivableService>();
        services.AddScoped<IDisputeManagementService, Warehouse3PL.Infrastructure.Services.Billing.DisputeManagementService>();

        // Analytics Foundation & Read Models (Phase 6.0 & Phase 6.3)
        services.AddScoped<IAnalyticsProjectionService, Warehouse3PL.Infrastructure.Services.Analytics.AnalyticsProjectionService>();
        services.AddScoped<IAnalyticsQueryService, Warehouse3PL.Infrastructure.Services.Analytics.AnalyticsQueryService>();
        services.AddScoped<ISlottingOptimizationService, Warehouse3PL.Infrastructure.Services.Analytics.SlottingOptimizationService>();
        services.AddScoped<IProductAffinityMiningService, Warehouse3PL.Infrastructure.Services.Analytics.ProductAffinityMiningService>();
        services.AddScoped<IRelocationRecommendationService, Warehouse3PL.Infrastructure.Services.Analytics.RelocationRecommendationService>();
        services.AddScoped<ITransportationProjectionService, Warehouse3PL.Infrastructure.Services.Analytics.TransportationProjectionService>();
        services.AddScoped<IDelayAttributionService, Warehouse3PL.Infrastructure.Services.Analytics.DelayAttributionService>();
        services.AddScoped<Warehouse3PL.Infrastructure.Services.Analytics.TransportationAnalyticsAuthorization>();
        services.AddScoped<IClaimsReadinessService, Warehouse3PL.Infrastructure.Services.Analytics.ClaimsReadinessService>();
        services.AddScoped<ICarrierScorecardService, Warehouse3PL.Infrastructure.Services.Analytics.CarrierScorecardService>();
        services.AddSingleton<IFxRateProvider, Warehouse3PL.Infrastructure.Services.Analytics.FxRateProvider>();
        services.AddScoped<IFreightReconciliationService, Warehouse3PL.Infrastructure.Services.Analytics.FreightReconciliationService>();
        services.AddScoped<Warehouse3PL.Infrastructure.Services.Analytics.MonitoringService>();
        services.AddScoped<IMonitoringService>(sp => sp.GetRequiredService<Warehouse3PL.Infrastructure.Services.Analytics.MonitoringService>());
        if (configuration.GetValue<bool>("Monitoring:Enabled")) services.AddHostedService<Warehouse3PL.Infrastructure.BackgroundJobs.MonitoringEvaluationWorker>();
        services.AddScoped<IDecisionIntelligenceService, Warehouse3PL.Infrastructure.Services.Analytics.DecisionIntelligenceService>();
        services.AddScoped<IProfitabilityIntelligenceService, Warehouse3PL.Infrastructure.Services.Analytics.ProfitabilityIntelligenceService>();
        services.AddScoped<IFreightCostVarianceService, Warehouse3PL.Infrastructure.Services.Analytics.FreightCostVarianceService>();

        // Private Object Storage & Carrier Webhook Config
        services.Configure<Warehouse3PL.Infrastructure.Services.Storage.ObjectStorageOptions>(
            configuration.GetSection(Warehouse3PL.Infrastructure.Services.Storage.ObjectStorageOptions.SectionName));
        services.Configure<CarrierWebhookOptions>(
            configuration.GetSection(CarrierWebhookOptions.SectionName));
        services.AddSingleton<IObjectStorageService, Warehouse3PL.Infrastructure.Services.Storage.PrivateObjectStorageService>();

        // 2. Persistence (PostgreSQL + EF Core)
        services.AddScoped<AuditableEntityInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=warehouse3pl_db;Username=postgres;Password=postgres";

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DbInitializer>();

        // 3. JWT & Keycloak Authentication
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            if (jwtOptions.UseKeycloak && !string.IsNullOrWhiteSpace(jwtOptions.KeycloakAuthority))
            {
                options.Authority = jwtOptions.KeycloakAuthority;
                options.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
                options.Audience = jwtOptions.Audience;
            }
            else
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                    ClockSkew = TimeSpan.Zero
                };
            }
        });

        // 4. RBAC & Dynamic Permission Authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // 5. Caching (Redis + resilient MemoryCache fallback)
        var cacheSection = configuration.GetSection(CacheOptions.SectionName);
        services.Configure<CacheOptions>(cacheSection);
        var cacheOptions = cacheSection.Get<CacheOptions>() ?? new CacheOptions();

        if (cacheOptions.EnableRedis && !string.IsNullOrWhiteSpace(cacheOptions.ConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = cacheOptions.ConnectionString;
                options.InstanceName = cacheOptions.InstanceName;
            });
        }

        services.AddSingleton<ICacheService, RedisCacheService>();

        // 6. Background Jobs (Hangfire)
        services.AddWarehouseHangfire(configuration);
        services.AddSingleton<IBackgroundJobService, BackgroundJobService>();

        return services;
    }
}
