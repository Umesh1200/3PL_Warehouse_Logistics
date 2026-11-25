using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;

internal static class Mdm02PostgresCheck
{
    public static async Task RunAsync(
        Func<ApplicationDbContext> createDb,
        HttpClient client,
        IObjectStorageService storageService,
        Guid client1Id,
        Guid client2Id,
        Guid facilityId,
        Guid foreignFacilityId,
        Guid activePodPackageId,
        Guid supersededPodPackageId,
        Guid driverAId,
        Guid driverBId,
        Func<HttpMethod, string, Guid, Guid?, string, object?, HttpRequestMessage> staffReq,
        Guid staffId,
        Action<DateTime?> setClockUtc,
        Action<List<Claim>?> setCustomClaims)
    {
        setClockUtc(null);

        // 1. Fetch pre-signed download URL via GET /pod as authorized Client 1
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", client1Id.ToString()),
            new Claim("permissions", Permissions.Dispatch.View)
        ]);

        string sigUrl;
        using (var podResp = await client.GetAsync($"/api/v1/outbound/dispatch/packages/{activePodPackageId}/pod"))
        {
            if (podResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-0.2 PostgreSQL GET /pod failed: {podResp.StatusCode}");

            using var doc = JsonDocument.Parse(await podResp.Content.ReadAsStringAsync());
            sigUrl = doc.RootElement.GetProperty("preSignedSignatureUrl").GetString()
                ?? throw new InvalidOperationException("MDM-0.2 PostgreSQL expected preSignedSignatureUrl.");
        }

        // Unauthenticated download must return 401
        setCustomClaims(null);
        using (var anonReq = new HttpRequestMessage(HttpMethod.Get, sigUrl))
        {
            anonReq.Headers.Add("X-Test-Anonymous", "true");
            using var anonResp = await client.SendAsync(anonReq);
            if (anonResp.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException($"MDM-0.2 PostgreSQL unauthenticated download expected 401, got {anonResp.StatusCode}.");
        }

        // 2. Authorized Client 1 downloads valid POD PNG -> 200 OK + security headers + valid PNG header
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", client1Id.ToString()),
            new Claim("permissions", Permissions.Dispatch.View)
        ]);
        using (var okClient = await client.GetAsync(sigUrl))
        {
            var bytes = await okClient.Content.ReadAsByteArrayAsync();
            if (okClient.StatusCode != HttpStatusCode.OK ||
                okClient.Headers.GetValues("X-Content-Type-Options").FirstOrDefault() != "nosniff" ||
                bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50)
            {
                throw new InvalidOperationException($"MDM-0.2 PostgreSQL Client1 download failed: {okClient.StatusCode}");
            }
        }

        // 3. Cross-Client (Client 2 holding Client 1's valid pre-signed URL) MUST receive 403 without disclosure
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", client2Id.ToString()),
            new Claim("permissions", Permissions.Dispatch.View)
        ]);
        using (var crossClient = await client.GetAsync(sigUrl))
        {
            var body = await crossClient.Content.ReadAsStringAsync();
            if (crossClient.StatusCode != HttpStatusCode.Forbidden ||
                body.Contains(client1Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"MDM-0.2 PostgreSQL cross-client expected 403 without disclosure, got {crossClient.StatusCode}");
            }
        }

        // 4. Cross-Facility Staff vs Assigned Staff vs Assigned Driver vs Unassigned Driver
        setCustomClaims(null);
        if ((await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, facilityId, Permissions.Dispatch.View, null))).StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("MDM-0.2 PostgreSQL assigned facility Staff download expected 200.");

        if ((await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, foreignFacilityId, Permissions.Dispatch.View, null))).StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException("MDM-0.2 PostgreSQL foreign facility Staff expected 403.");

        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, driverAId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.Driver),
            new Claim("user_type", "Driver"),
            new Claim("facility_id", facilityId.ToString()),
            new Claim("permissions", Permissions.Dispatch.ExecuteDelivery)
        ]);
        if ((await client.GetAsync(sigUrl)).StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("MDM-0.2 PostgreSQL assigned Driver download expected 200.");

        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, driverBId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.Driver),
            new Claim("user_type", "Driver"),
            new Claim("facility_id", facilityId.ToString()),
            new Claim("permissions", Permissions.Dispatch.ExecuteDelivery)
        ]);
        if ((await client.GetAsync(sigUrl)).StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException("MDM-0.2 PostgreSQL unassigned Driver download expected 403.");

        // 4B. MDM02-R01 Staff permission parity negatives:
        // - Inbound-only Staff -> 403
        // - Cross-facility analytics-only Staff (MultiFacilityView + ControlTower + Analytics:CrossFacility without Dispatch.View) -> 403
        // - Foreign-facility Dispatch.View + legacy Analytics:CrossFacility (without MultiFacilityView) -> 403
        // - Foreign-facility Dispatch.View + MultiFacilityView -> 200 (parity with GET /pod)
        if ((await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, facilityId, $"{Permissions.Inbound.View},{Permissions.Inbound.Receive}", null))).StatusCode != HttpStatusCode.Forbidden ||
            (await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, foreignFacilityId, $"{Permissions.Analytics.MultiFacilityView},{Permissions.Analytics.ControlTowerView},Analytics:CrossFacility", null))).StatusCode != HttpStatusCode.Forbidden ||
            (await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, foreignFacilityId, $"{Permissions.Dispatch.View},Analytics:CrossFacility", null))).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM02-R01 PostgreSQL unauthorized inbound/analytics Staff check expected 403.");
        }

        if ((await client.SendAsync(staffReq(HttpMethod.Get, sigUrl, staffId, foreignFacilityId, $"{Permissions.Dispatch.View},{Permissions.Analytics.MultiFacilityView}", null))).StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException("MDM02-R01 PostgreSQL Staff with Dispatch.View + MultiFacilityView expected 200.");
        }

        // 5. Historical superseded POD evidence remains accessible to Staff/Client, denied to Driver
        setCustomClaims(null);
        await using (var pgCheckDb = createDb())
        {
            var supOldPod = await pgCheckDb.ProofOfDeliveries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.PackageId == supersededPodPackageId && p.IsSuperseded && p.SignatureObjectKey != null)
                .FirstOrDefaultAsync();

            if (supOldPod != null)
            {
                var supOldUrl = (await storageService.GeneratePreSignedDownloadUrlAsync(supOldPod.SignatureObjectKey!, TimeSpan.FromMinutes(15))).DownloadUrl;
                if ((await client.SendAsync(staffReq(HttpMethod.Get, supOldUrl, staffId, facilityId, Permissions.Dispatch.View, null))).StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("MDM02-R01 PostgreSQL superseded historical POD Staff read expected 200.");

                setCustomClaims([
                    new Claim(ClaimTypes.NameIdentifier, driverAId.ToString()),
                    new Claim(ClaimTypes.Role, SystemRoles.Driver),
                    new Claim("user_type", "Driver"),
                    new Claim("facility_id", facilityId.ToString()),
                    new Claim("permissions", Permissions.Dispatch.ExecuteDelivery)
                ]);
                if ((await client.GetAsync(supOldUrl)).StatusCode != HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("MDM02-R01 PostgreSQL superseded historical POD Driver read expected 403.");
                setCustomClaims(null);
            }
        }

        // 5B. MDM02-R01 (Pending/Revoked/Invalid Custody History) & MDM02-R02 (Staged-Only / Unfinalized / SHA-256 Tamper)
        var pngSample = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 11, 22, 33, 44 };
        var tamperedPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 99, 88, 77, 66 };
        var pngShaHex = Convert.ToHexString(SHA256.HashData(pngSample)).ToLowerInvariant();

        var keyPending = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_pending.png";
        var keyRevoked = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_revoked.png";
        var keyMissingXfer = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_missing_xfer.png";
        var keyReassignedXfer = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_reassigned_xfer.png";
        var keyStagedOnly = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_staged_only.png";
        var keyUnfinalized = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_unfinalized.png";
        var keyHashMismatch = $"tenants/{client1Id}/pod/{Guid.NewGuid()}/pg_hash_mismatch.png";

        foreach (var fk in new[] { keyPending, keyRevoked, keyMissingXfer, keyReassignedXfer, keyUnfinalized })
        {
            var tk = "pg_tmp_" + Guid.NewGuid().ToString("N") + ".png";
            await storageService.UploadStagedAsync(tk, new MemoryStream(pngSample), "image/png");
            await storageService.FinalizeObjectAsync(tk, fk);
        }
        await storageService.UploadStagedAsync(keyStagedOnly, new MemoryStream(pngSample), "image/png");
        if (await storageService.DownloadFinalizedAsync(keyStagedOnly) != null)
            throw new InvalidOperationException("MDM02-R02 PostgreSQL DownloadFinalizedAsync must return null for staged-only object.");

        var hshTmp = "pg_tmp_" + Guid.NewGuid().ToString("N") + ".png";
        await storageService.UploadStagedAsync(hshTmp, new MemoryStream(tamperedPng), "image/png");
        await storageService.FinalizeObjectAsync(hshTmp, keyHashMismatch);

        await using (var r02SeedDb = createDb())
        {
            var ordR = new OutboundOrder { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, OrderNumber = "PG-ORD-R01R02-" + Guid.NewGuid().ToString("N")[..6], CustomerName = "PG Auditor", Status = OutboundOrderStatus.Completed };
            r02SeedDb.OutboundOrders.Add(ordR);

            var shpPending = new Shipment { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, ShipmentNumber = "PG-SHP-PND-" + Guid.NewGuid().ToString("N")[..6], CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, AssignedDriverUserId = driverAId, CustodyStatus = ShipmentCustodyStatus.PendingHandover, Version = 1 };
            var shpRevoked = new Shipment { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, ShipmentNumber = "PG-SHP-REV-" + Guid.NewGuid().ToString("N")[..6], CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, AssignedDriverUserId = driverAId, CustodyStatus = ShipmentCustodyStatus.Revoked, Version = 1 };
            var shpMissXfer = new Shipment { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, ShipmentNumber = "PG-SHP-MXF-" + Guid.NewGuid().ToString("N")[..6], CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, AssignedDriverUserId = driverAId, CustodyStatus = ShipmentCustodyStatus.Confirmed, Version = 1 };
            var shpReassXfer = new Shipment { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, ShipmentNumber = "PG-SHP-RXF-" + Guid.NewGuid().ToString("N")[..6], CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, AssignedDriverUserId = driverAId, CustodyStatus = ShipmentCustodyStatus.Confirmed, Version = 1 };
            r02SeedDb.Shipments.AddRange(shpPending, shpRevoked, shpMissXfer, shpReassXfer);

            r02SeedDb.ShipmentCustodyTransfers.Add(new ShipmentCustodyTransfer
            {
                ClientId = client1Id, FacilityId = facilityId, ShipmentId = shpReassXfer.Id, CarrierCode = "SIM-FEDEX",
                DriverName = "Reassigned", DriverSignatureAcknowledgment = "SUPERVISOR_CUSTODY_REASSIGNMENT:pg-test",
                TotalPackagesConfirmed = 1, TotalWeightKg = 1m, AuthorizedByUserId = staffId, AssignedDriverUserId = driverAId,
                CustodyStatus = ShipmentCustodyStatus.Confirmed, TransferredAtUtc = DateTime.UtcNow
            });

            void AddPgPodCase(Shipment shp, string key, bool finalized, string tag)
            {
                var hu = new HandlingUnit { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, LpnCode = "PG-HU-" + tag, UnitType = HandlingUnitType.ShippingCarton };
                var pkg = new Package { Id = Guid.NewGuid(), ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = ordR.Id, ShipmentId = shp.Id, HandlingUnitId = hu.Id, PackageNumber = "PG-PKG-" + tag, CarrierCode = "SIM-FEDEX", TrackingNumber = "PG-TRK-" + tag, Status = PackageStatus.Loaded, GrossWeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10 };
                r02SeedDb.HandlingUnits.Add(hu);
                r02SeedDb.Packages.Add(pkg);
                r02SeedDb.ProofOfDeliveries.Add(new ProofOfDelivery
                {
                    ClientId = client1Id, FacilityId = facilityId, PackageId = pkg.Id, ShipmentId = shp.Id,
                    TrackingNumber = pkg.TrackingNumber!, CarrierCode = "SIM-FEDEX", RecipientName = "PG Recip", IsSigned = true,
                    SignatureObjectKey = key, SignatureContentType = "image/png", SignatureSha256 = pngShaHex,
                    IdempotencyKey = "PG-IDEMP-" + tag, EvidenceSource = PodEvidenceSource.DriverHandheld,
                    DeliveredAtUtc = DateTime.UtcNow, IsStorageFinalized = finalized
                });
            }

            AddPgPodCase(shpPending, keyPending, true, "PND-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpRevoked, keyRevoked, true, "REV-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpMissXfer, keyMissingXfer, true, "MXF-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpReassXfer, keyReassignedXfer, true, "RXF-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpMissXfer, keyStagedOnly, true, "STG-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpMissXfer, keyUnfinalized, false, "UNF-" + Guid.NewGuid().ToString("N")[..5]);
            AddPgPodCase(shpMissXfer, keyHashMismatch, true, "HSH-" + Guid.NewGuid().ToString("N")[..5]);

            await r02SeedDb.SaveChangesAsync();
        }

        // Driver negative custody checks (PendingHandover, Revoked, Missing CustodyTransfer, Supervisor Reassignment Transfer)
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, driverAId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.Driver),
            new Claim("user_type", "Driver"),
            new Claim("facility_id", facilityId.ToString()),
            new Claim("permissions", Permissions.Dispatch.ExecuteDelivery)
        ]);
        foreach (var badKey in new[] { keyPending, keyRevoked, keyMissingXfer, keyReassignedXfer })
        {
            var u = (await storageService.GeneratePreSignedDownloadUrlAsync(badKey, TimeSpan.FromMinutes(15))).DownloadUrl;
            if ((await client.GetAsync(u)).StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"MDM02-R01 PostgreSQL bad-custody driver check expected 403 for {badKey}.");
        }

        // MDM02-R02 evidence integrity negative checks as authorized Staff
        setCustomClaims(null);
        var uStgPg = (await storageService.GeneratePreSignedDownloadUrlAsync(keyStagedOnly, TimeSpan.FromMinutes(15))).DownloadUrl;
        using (var stgResp = await client.SendAsync(staffReq(HttpMethod.Get, uStgPg, staffId, facilityId, Permissions.Dispatch.View, null)))
        {
            var stgBytes = await stgResp.Content.ReadAsByteArrayAsync();
            if (stgResp.StatusCode != HttpStatusCode.NotFound || (stgBytes.Length >= 8 && stgBytes[0] == 0x89 && stgBytes[1] == 0x50))
                throw new InvalidOperationException($"MDM02-R02 PostgreSQL staged-only fallback check failed: {stgResp.StatusCode}");
        }

        var uUnfPg = (await storageService.GeneratePreSignedDownloadUrlAsync(keyUnfinalized, TimeSpan.FromMinutes(15))).DownloadUrl;
        using (var unfResp = await client.SendAsync(staffReq(HttpMethod.Get, uUnfPg, staffId, facilityId, Permissions.Dispatch.View, null)))
        {
            var unfBytes = await unfResp.Content.ReadAsByteArrayAsync();
            if (unfResp.StatusCode != HttpStatusCode.Forbidden || (unfBytes.Length >= 8 && unfBytes[0] == 0x89 && unfBytes[1] == 0x50))
                throw new InvalidOperationException($"MDM02-R02 PostgreSQL unfinalized POD check expected 403 without PNG bytes, got {unfResp.StatusCode}");
        }

        var uHshPg = (await storageService.GeneratePreSignedDownloadUrlAsync(keyHashMismatch, TimeSpan.FromMinutes(15))).DownloadUrl;
        using (var hshResp = await client.SendAsync(staffReq(HttpMethod.Get, uHshPg, staffId, facilityId, Permissions.Dispatch.View, null)))
        {
            var hshBytes = await hshResp.Content.ReadAsByteArrayAsync();
            if (hshResp.StatusCode != HttpStatusCode.Forbidden || (hshBytes.Length >= 8 && hshBytes[0] == 0x89 && hshBytes[1] == 0x50))
                throw new InvalidOperationException($"MDM02-R02 PostgreSQL hash-mismatch check expected 403 without PNG bytes, got {hshResp.StatusCode}");
        }

        // 6. Tampered token, path traversal, and expired token rejected 403
        setCustomClaims([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
            new Claim("user_type", "Client"),
            new Claim("tenant_id", client1Id.ToString()),
            new Claim("permissions", Permissions.Dispatch.View)
        ]);
        if ((await client.GetAsync(sigUrl + "tamper")).StatusCode != HttpStatusCode.Forbidden ||
            (await client.GetAsync("/api/v1/storage/download?key=../../etc/passwd&token=bad")).StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("MDM-0.2 PostgreSQL tampered/traversal check expected 403.");
        }

        // Advance controllable clock past 15-minute expiry -> 403 Forbidden
        setClockUtc(DateTime.UtcNow.AddMinutes(30));
        try
        {
            if ((await client.GetAsync(sigUrl)).StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("MDM-0.2 PostgreSQL expired download token expected 403 Forbidden.");
        }
        finally
        {
            setClockUtc(null);
            setCustomClaims(null);
        }

        // Suppress unused warning if storageService verified directly
        _ = await storageService.GeneratePreSignedDownloadUrlAsync("tenants/smoke/pod/p/s.png");
        Console.WriteLine("PASS PostgreSQL MDM-0.2 Storage Download Endpoint, Signing Key Hardening, Expiry/Tamper & Cross-Tenant/Facility Isolation verified cleanly.");
    }
}
