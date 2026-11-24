using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

internal static class Mdm01PostgresCheck
{
    public static async Task RunAsync(
        string connectionString,
        HttpClient client,
        Guid clientId,
        Guid existingUsFacilityId,
        Func<HttpMethod, string, Guid, Guid?, string, object?, HttpRequestMessage> staffReq,
        Guid staffOperatorId,
        Action<List<Claim>?> setCustomClaims)
    {
        var npFacId = Guid.NewGuid();
        var badFacId = Guid.NewGuid();
        var npOrderId = Guid.NewGuid();
        var npPackageId = Guid.NewGuid();
        var badOrderId = Guid.NewGuid();
        var badPackageId = Guid.NewGuid();

        var identity = new Mdm01Identity();
        var auditInterceptor = new AuditableEntityInterceptor(identity, new Mdm01Clock());
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(auditInterceptor)
            .Options;

        await using (var db = new ApplicationDbContext(opts, new Mdm01Tenant(), identity, auditInterceptor))
        {
            db.Facilities.AddRange(
                new Facility
                {
                    Id = npFacId,
                    FacilityCode = "PG-KTM-MDM01",
                    Name = "Kathmandu MDM Hub",
                    AddressLine1 = "Ring Road, Kalanki",
                    AddressLine2 = "Gate B",
                    City = "Kathmandu",
                    StateOrProvince = "Bagmati",
                    PostalCode = "44600",
                    CountryCode = "NP",
                    Timezone = "Asia/Kathmandu",
                    ContactEmail = "ktm-pg@nexus3pl.test",
                    ContactPhone = "+977-1-5550101",
                    TotalAreaSqM = 5100m,
                    DockDoorCount = 5,
                    OperatingHoursJson = "{\"satClosed\":true}",
                    ConfigurationJson = "{\"scaleBaud\":9600,\"internalFlag\":true}",
                    IsActive = true
                },
                new Facility
                {
                    Id = badFacId,
                    FacilityCode = "PG-BAD-MDM01",
                    Name = "Incomplete MDM Hub",
                    AddressLine1 = null,
                    City = null,
                    CountryCode = "US",
                    Timezone = "UTC",
                    IsActive = true
                });

            db.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = npFacId,
                Status = FacilityAccessStatus.Active
            });

            var npHuId = Guid.NewGuid();
            var badHuId = Guid.NewGuid();
            db.HandlingUnits.AddRange(
                new HandlingUnit { Id = npHuId, ClientId = clientId, FacilityId = npFacId, LpnCode = "PG-MDM01-HU1", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = badHuId, ClientId = clientId, FacilityId = badFacId, LpnCode = "PG-MDM01-HU2", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse });

            db.OutboundOrders.AddRange(
                new OutboundOrder
                {
                    Id = npOrderId,
                    ClientId = clientId,
                    FacilityId = npFacId,
                    OrderNumber = "PG-MDM01-ORD-NP",
                    CustomerName = "Pokhara Store",
                    Status = OutboundOrderStatus.InProgress,
                    ShippingAddress = new ShippingAddress
                    {
                        OutboundOrderId = npOrderId,
                        RecipientName = "Ram Thapa",
                        AddressLine1 = "New Road 9",
                        City = "Pokhara",
                        StateOrProvince = "Gandaki",
                        PostalCode = "33700",
                        CountryCode = "NPL"
                    }
                },
                new OutboundOrder
                {
                    Id = badOrderId,
                    ClientId = clientId,
                    FacilityId = badFacId,
                    OrderNumber = "PG-MDM01-ORD-BAD",
                    CustomerName = "No Origin Buyer",
                    Status = OutboundOrderStatus.InProgress,
                    ShippingAddress = new ShippingAddress
                    {
                        OutboundOrderId = badOrderId,
                        RecipientName = "Buyer",
                        AddressLine1 = "10 Dest St",
                        City = "Dallas",
                        StateOrProvince = "TX",
                        PostalCode = "75201",
                        CountryCode = "US"
                    }
                });

            db.Packages.AddRange(
                new Package { Id = npPackageId, ClientId = clientId, FacilityId = npFacId, OutboundOrderId = npOrderId, HandlingUnitId = npHuId, PackageNumber = "PG-MDM01-PKG-NP", LengthCm = 30m, WidthCm = 20m, HeightCm = 15m, GrossWeightKg = 2.0m, Status = PackageStatus.Packed },
                new Package { Id = badPackageId, ClientId = clientId, FacilityId = badFacId, OutboundOrderId = badOrderId, HandlingUnitId = badHuId, PackageNumber = "PG-MDM01-PKG-BAD", LengthCm = 30m, WidthCm = 20m, HeightCm = 15m, GrossWeightKg = 2.0m, Status = PackageStatus.Packed });

            await db.SaveChangesAsync();
        }

        // 1. Reader-safe FacilityDto contains Nepal address fields, NEVER configurationJson/operatingHoursJson
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", clientId.ToString()),
            new Claim("permissions", Permissions.Facilities.View),
            new Claim("permissions", Permissions.Facilities.Manage)
        ]);
        using (var clientGet = await client.GetAsync($"/api/v1/facilities/{npFacId}"))
        {
            var body = await clientGet.Content.ReadAsStringAsync();
            if (clientGet.StatusCode != HttpStatusCode.OK ||
                body.Contains("configurationJson", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("operatingHoursJson", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("scaleBaud", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"MDM-0.1 PostgreSQL FacilityDto exposed configuration or failed: {clientGet.StatusCode} {body}");
            }
            var dto = JsonSerializer.Deserialize<FacilityDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (dto.CountryCode != "NP" || dto.PostalCode != "44600" || dto.ContactEmail != "ktm-pg@nexus3pl.test" || dto.DockDoorCount != 5)
                throw new InvalidOperationException("MDM-0.1 PostgreSQL FacilityDto missing expected Nepal address/contact metadata.");
        }

        // Client with legacy Manage claim MUST still be denied 403 on privileged configuration endpoint
        if ((await client.GetAsync($"/api/v1/facilities/{npFacId}/configuration")).StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException("MDM-0.1 PostgreSQL Client must be denied 403 on /configuration.");
        setCustomClaims(null);

        // Authorized assigned Staff with facilities:manage gets 200 OK with ConfigurationJson
        using (var staffCfg = await client.SendAsync(staffReq(HttpMethod.Get, $"/api/v1/facilities/{npFacId}/configuration", staffOperatorId, npFacId, Permissions.Facilities.Manage, null)))
        {
            if (staffCfg.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-0.1 PostgreSQL Staff /configuration expected 200 OK, got {staffCfg.StatusCode}");
            var cfg = JsonSerializer.Deserialize<FacilityConfigurationDto>(await staffCfg.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (string.IsNullOrEmpty(cfg.ConfigurationJson) || !cfg.ConfigurationJson.Contains("scaleBaud"))
                throw new InvalidOperationException("MDM-0.1 PostgreSQL /configuration missing ConfigurationJson.");
        }

        // 2. Shipping rate & label generation use persisted Nepal origin and normalize destination NPL->NP without mutating DB
        using (var npRate = await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/outbound/shipping/rates", staffOperatorId, npFacId, Permissions.Outbound.Ship, new { packageId = npPackageId })))
        {
            if (npRate.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-0.1 PostgreSQL Nepal shipping rate failed: {npRate.StatusCode}");
        }

        using (var npLbl = await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", staffOperatorId, npFacId, Permissions.Outbound.Ship,
            new { packageId = npPackageId, carrierCode = "SIM-FEDEX", serviceLevel = 1, labelFormat = "ZPL", idempotencyKey = "pg-mdm01-np-lbl", simulateFailure = false })))
        {
            if (npLbl.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-0.1 PostgreSQL Nepal label generation failed: {npLbl.StatusCode}");
        }

        // Missing origin facility address fails closed with 400 BadRequest and zero CarrierOperationLogs
        if ((await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/outbound/shipping/rates", staffOperatorId, badFacId, Permissions.Outbound.Ship, new { packageId = badPackageId }))).StatusCode != HttpStatusCode.BadRequest ||
            (await client.SendAsync(staffReq(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", staffOperatorId, badFacId, Permissions.Outbound.Ship,
                new { packageId = badPackageId, carrierCode = "SIM-FEDEX", serviceLevel = 1 }))).StatusCode != HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException("MDM-0.1 PostgreSQL missing facility origin expected 400 BadRequest.");
        }

        await using var verifyDb = new ApplicationDbContext(opts, new Mdm01Tenant(), identity, auditInterceptor);
        var opLog = await verifyDb.CarrierOperationLogs.SingleAsync(l => l.PackageId == npPackageId);
        using var reqDoc = JsonDocument.Parse(opLog.RequestPayload!);
        var origin = reqDoc.RootElement.GetProperty("OriginAddress");
        var dest = reqDoc.RootElement.GetProperty("DestinationAddress");
        if (origin.GetProperty("City").GetString() != "Kathmandu" ||
            origin.GetProperty("StreetLine1").GetString() != "Ring Road, Kalanki" ||
            origin.GetProperty("CountryCode").GetString() != "NP" ||
            dest.GetProperty("CountryCode").GetString() != "NP")
        {
            throw new InvalidOperationException($"MDM-0.1 PostgreSQL persisted request payload mismatch: {opLog.RequestPayload}");
        }

        if (await verifyDb.CarrierOperationLogs.AnyAsync(l => l.PackageId == badPackageId))
            throw new InvalidOperationException("MDM-0.1 PostgreSQL failed validation still wrote CarrierOperationLog!");

        // Verify legacy US facility row still preserves CountryCode="USA" untouched in PostgreSQL
        var usFac = await verifyDb.Facilities.SingleAsync(f => f.Id == existingUsFacilityId);
        if (usFac.CountryCode != "USA")
            throw new InvalidOperationException($"MDM-0.1 PostgreSQL silently rewrote US facility CountryCode to '{usFac.CountryCode}'.");

        Console.WriteLine("PASS PostgreSQL MDM-0.1 Real Facility Origin (NP/US), Fail-Closed Missing Address, & FacilityDto Authorization verified cleanly.");
    }

    private sealed class Mdm01Identity : ICurrentUserContext
    {
        public Guid? UserId => Guid.Empty;
        public string? Email => "mdm01@verify.local";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => true;
    }

    private sealed class Mdm01Tenant : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) { }
    }

    private sealed class Mdm01Clock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
