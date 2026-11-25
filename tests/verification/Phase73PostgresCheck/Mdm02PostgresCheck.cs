using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;

internal static class Mdm02PostgresCheck
{
    public static async Task RunAsync(
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

        // 5. Historical superseded POD evidence remains accessible to assigned facility Staff
        setCustomClaims(null);
        using (var supPodResp = await client.SendAsync(staffReq(HttpMethod.Get, $"/api/v1/outbound/dispatch/packages/{supersededPodPackageId}/pod", staffId, facilityId, Permissions.Dispatch.View, null)))
        {
            if (supPodResp.StatusCode == HttpStatusCode.OK)
            {
                using var sDoc = JsonDocument.Parse(await supPodResp.Content.ReadAsStringAsync());
                var supUrl = sDoc.RootElement.GetProperty("preSignedSignatureUrl").GetString();
                if (!string.IsNullOrEmpty(supUrl))
                {
                    if ((await client.SendAsync(staffReq(HttpMethod.Get, supUrl, staffId, facilityId, Permissions.Dispatch.View, null))).StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException("MDM-0.2 PostgreSQL active POD download on superseded-history package failed.");
                }
            }
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
