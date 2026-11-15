using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.Infrastructure.Services.Storage;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class ProofOfDeliverySubmissionTests : IDisposable
{
    private readonly string _storageDir;
    private readonly PrivateObjectStorageService _storageService;

    public ProofOfDeliverySubmissionTests()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), "pod_tests_storage_" + Guid.NewGuid().ToString("N"));
        var options = new ObjectStorageOptions
        {
            BaseDirectory = _storageDir,
            SigningSecretKey = "TestSecretKey123456789012345678901234567890!",
            DefaultExpiryMinutes = 15,
            MinExpiryMinutes = 1,
            MaxExpiryMinutes = 60,
            MaxFileSizeBytes = 5 * 1024 * 1024
        };

        _storageService = new PrivateObjectStorageService(
            Options.Create(options),
            NullLogger<PrivateObjectStorageService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_storageDir))
            {
                Directory.Delete(_storageDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private class TestEnvFixture
    {
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid LocationId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public string DbName { get; set; } = Guid.NewGuid().ToString();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var client = new Client { Id = fix.ClientId, CompanyName = "Acme Corp", AccountCode = "ACME" };
        var facility = new Facility { Id = fix.FacilityId, Name = "Chicago DC", FacilityCode = "ORD1" };
        var loc = new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-STAGE-01",
            Barcode = "LOC-STAGE-01",
            LocationType = LocationType.Bin,
            IsPickable = false
        };
        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-POD-001",
            Description = "Proof of Delivery Test Item",
            LengthCm = 10,
            WidthCm = 10,
            HeightCm = 10,
            WeightKg = 1.0m
        };
        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = fix.LocationId,
            ItemId = fix.ItemId,
            QtyAllocated = 20m,
            QtyAvailable = 80m
        };

        context.Clients.Add(client);
        context.Facilities.Add(facility);
        context.Locations.Add(loc);
        context.Items.Add(item);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package)> CreateDispatchedPackageAsync(
        TestEnvFixture fix,
        string trackingNumber = "TRK-POD-1001",
        string carrierCode = "SIM-FEDEX")
    {
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-POD-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
            Status = OutboundOrderStatus.Completed, // Fulfillment completed at warehouse
            DeliveryStatus = OrderDeliveryStatus.Dispatched
        };
        context.OutboundOrders.Add(order);

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = "SHP-POD-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
            CarrierCode = carrierCode,
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        context.Shipments.Add(shipment);

        var hu = new HandlingUnit
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LpnCode = "HU-POD-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse,
            CurrentLocationId = fix.LocationId,
            CurrentOutboundOrderId = order.Id
        };
        context.HandlingUnits.Add(hu);

        var package = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = order.Id,
            ShipmentId = shipment.Id,
            HandlingUnitId = hu.Id,
            PackageNumber = "PKG-POD-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
            Status = PackageStatus.Loaded,
            TrackingNumber = trackingNumber,
            CarrierCode = carrierCode,
            CarrierServiceCode = "GROUND",
            CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
        };
        context.Packages.Add(package);

        await context.SaveChangesAsync();
        return (order, shipment, package);
    }

    private static byte[] CreateValidPngBytes(int payloadSize = 50)
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] payload = new byte[pngHeader.Length + payloadSize];
        Array.Copy(pngHeader, payload, pngHeader.Length);
        for (int i = pngHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    private static byte[] CreateValidJpegBytes(int payloadSize = 50)
    {
        byte[] jpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];
        byte[] payload = new byte[jpegHeader.Length + payloadSize];
        Array.Copy(jpegHeader, payload, jpegHeader.Length);
        for (int i = jpegHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    private static byte[] CreateValidPdfBytes(int payloadSize = 50)
    {
        byte[] pdfHeader = [0x25, 0x50, 0x44, 0x46]; // %PDF
        byte[] payload = new byte[pdfHeader.Length + payloadSize];
        Array.Copy(pdfHeader, payload, pdfHeader.Length);
        for (int i = pdfHeader.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    [Fact]
    public async Task SubmitProofOfDelivery_CarrierApi_Success_CreatesAuthoritativePod()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "FDX-CONF-12345",
            CarrierEventId = "EVT-CARRIER-999",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "Robert Langdon",
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-30)
        };

        var result = await service.SubmitProofOfDeliveryAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.NotEqual(Guid.Empty, result.ProofOfDeliveryId);
        Assert.Equal(pkg.Id, result.PackageId);
        Assert.Equal(pkg.TrackingNumber, result.TrackingNumber);
        Assert.True(result.IsStorageFinalized);

        // Verify entity persisted in DB
        var savedPod = await context.ProofOfDeliveries.FirstOrDefaultAsync(p => p.Id == result.ProofOfDeliveryId);
        Assert.NotNull(savedPod);
        Assert.Equal("FDX-CONF-12345", savedPod.CarrierConfirmationNumber);
        Assert.Equal("EVT-CARRIER-999", savedPod.CarrierEventId);
        Assert.Equal("Robert Langdon", savedPod.RecipientName);
        Assert.Equal(PodEvidenceSource.CarrierApi, savedPod.EvidenceSource);
        Assert.False(savedPod.IsSuperseded);

        // Crucial Sub-Step 3 Boundary Check: Delivery confirmation must remain out of scope
        // Package status is NOT changed to Delivered yet (reserved for Sub-Step 4)
        var packageAfter = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(packageAfter);
        Assert.Equal(TrackingMilestoneStatus.PendingHandover, packageAfter.CurrentTrackingStatus);
        Assert.Null(packageAfter.DeliveredAtUtc);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_DriverHandheld_WithSignatureAndPhoto_UploadsStagedAndFinalizes()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        byte[] sigData = CreateValidPngBytes();
        byte[] photoData = CreateValidJpegBytes();

        using var sigStream = new MemoryStream(sigData);
        using var photoStream = new MemoryStream(photoData);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            RecipientName = "Alice Cooper",
            DeliveryLocationDescription = "Front Porch dropoff",
            IsSigned = true,
            SignatureStream = sigStream,
            SignatureContentType = "image/png",
            PhotoStream = photoStream,
            PhotoContentType = "image/jpeg",
            Latitude = 41.8781m,
            Longitude = -87.6298m,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        var result = await service.SubmitProofOfDeliveryAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStorageFinalized);
        Assert.NotNull(result.SignatureSha256);
        Assert.NotNull(result.PhotoSha256);
        Assert.NotNull(result.PreSignedSignatureUrl);
        Assert.NotNull(result.PreSignedPhotoUrl);

        // Verify files promoted to final storage
        var savedPod = await context.ProofOfDeliveries.FirstOrDefaultAsync(p => p.Id == result.ProofOfDeliveryId);
        Assert.NotNull(savedPod);
        Assert.StartsWith("tenants/", savedPod.SignatureObjectKey);
        Assert.StartsWith("tenants/", savedPod.PhotoObjectKey);
        Assert.True(await _storageService.ExistsAsync(savedPod.SignatureObjectKey!));
        Assert.True(await _storageService.ExistsAsync(savedPod.PhotoObjectKey!));
    }

    [Fact]
    public async Task SubmitProofOfDelivery_ConsigneePortal_WithAcknowledgment_Succeeds()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Client }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.ConsigneePortal,
            RecipientName = "Receiving Officer Bob",
            ConsigneeAcknowledgmentText = "Shipment received intact at loading bay",
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-15)
        };

        var result = await service.SubmitProofOfDeliveryAsync(request);

        Assert.True(result.IsSuccess);
        var savedPod = await context.ProofOfDeliveries.FirstOrDefaultAsync(p => p.Id == result.ProofOfDeliveryId);
        Assert.NotNull(savedPod);
        Assert.Equal(PodEvidenceSource.ConsigneePortal, savedPod.EvidenceSource);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_ManualVerification_StaffUser_Succeeds()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        byte[] pdfBytes = CreateValidPdfBytes();
        using var pdfStream = new MemoryStream(pdfBytes);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.ManualVerification,
            VerificationReason = "Confirmed with carrier dispatch; paper BOL receipt attached",
            RecipientName = "Warehouse Lead",
            PhotoStream = pdfStream,
            PhotoContentType = "application/pdf",
            DeliveredAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        var result = await service.SubmitProofOfDeliveryAsync(request);

        Assert.True(result.IsSuccess);
        var savedPod = await context.ProofOfDeliveries.FirstOrDefaultAsync(p => p.Id == result.ProofOfDeliveryId);
        Assert.NotNull(savedPod);
        Assert.Equal(PodEvidenceSource.ManualVerification, savedPod.EvidenceSource);
        Assert.NotNull(savedPod.PhotoObjectKey);
        Assert.True(await _storageService.ExistsAsync(savedPod.PhotoObjectKey));
    }

    [Fact]
    public async Task SubmitProofOfDelivery_ManualVerification_ClientUser_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Client }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.ManualVerification,
            VerificationReason = "Unauthorized client trying to manual verify",
            DeliveredAtUtc = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<ForbiddenException>(() => service.SubmitProofOfDeliveryAsync(request));
    }

    [Fact]
    public async Task SubmitProofOfDelivery_SvgContentType_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        byte[] svgBytes = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'></svg>");
        using var stream = new MemoryStream(svgBytes);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            RecipientName = "Alice",
            SignatureStream = stream,
            SignatureContentType = "image/svg+xml"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.SubmitProofOfDeliveryAsync(request));
        Assert.Contains("SVG", ex.Message);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_SpoofedMimeType_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        // Raw text declared as image/png
        byte[] textBytes = Encoding.UTF8.GetBytes("Fake PNG payload here");
        using var stream = new MemoryStream(textBytes);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            RecipientName = "Alice",
            SignatureStream = stream,
            SignatureContentType = "image/png"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.SubmitProofOfDeliveryAsync(request));
        Assert.Contains("PNG signature", ex.Message);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_CrossTenantAttempt_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            TenantIdOverride = Guid.NewGuid(), // Different client
            CarrierConfirmationNumber = "FDX-12345",
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.SubmitProofOfDeliveryAsync(request));
        Assert.Contains("different client tenant", ex.Message);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_IdempotentDuplicate_ReturnsExistingPod()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request1 = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "DUP-TEST-CONF-001",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "Charlie Brown"
        };

        var result1 = await service.SubmitProofOfDeliveryAsync(request1);
        Assert.True(result1.IsSuccess);
        Assert.False(result1.IsDuplicate);

        // Submit second identical confirmation
        var request2 = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "DUP-TEST-CONF-001",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "Charlie Brown"
        };

        var result2 = await service.SubmitProofOfDeliveryAsync(request2);
        Assert.True(result2.IsSuccess);
        Assert.True(result2.IsDuplicate);
        Assert.Equal(result1.ProofOfDeliveryId, result2.ProofOfDeliveryId);

        // Verify count of PODs for package is still exactly 1
        int count = await context.ProofOfDeliveries.CountAsync(p => p.PackageId == pkg.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SubmitProofOfDelivery_ContradictoryConfirmation_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request1 = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "ORIGINAL-CONF-111",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "First Recipient"
        };
        await service.SubmitProofOfDeliveryAsync(request1);

        // Submit contradictory request with different confirmation number
        var request2 = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "CONTRADICTORY-CONF-999",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "First Recipient"
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.SubmitProofOfDeliveryAsync(request2));
        Assert.Contains("Contradictory delivery confirmation", ex.Message);
    }

    [Fact]
    public async Task SupersedeProofOfDelivery_ValidReplacement_SupersedesOldAndCreatesNewActivePod()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        // 1. Initial submission
        var initialRequest = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "OLD-CONF-001",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "Wrong Recipient"
        };
        var initialResult = await service.SubmitProofOfDeliveryAsync(initialRequest);

        // 2. Supersession request
        var supersedeRequest = new SupersedeProofOfDeliveryRequest
        {
            ExistingPodId = initialResult.ProofOfDeliveryId,
            Reason = "Customer reported misdelivery; actual delivery completed at neighbor address",
            ReplacementSubmission = new SubmitProofOfDeliveryRequest
            {
                PackageId = pkg.Id,
                CarrierConfirmationNumber = "NEW-CONF-002",
                EvidenceSource = PodEvidenceSource.CarrierApi,
                RecipientName = "Correct Recipient",
                DeliveredAtUtc = DateTime.UtcNow
            }
        };

        var supersedeResult = await service.SupersedeProofOfDeliveryAsync(supersedeRequest);
        Assert.True(supersedeResult.IsSuccess);
        Assert.NotEqual(initialResult.ProofOfDeliveryId, supersedeResult.ProofOfDeliveryId);

        // 3. Verify state in DB
        var oldPod = await context.ProofOfDeliveries.FindAsync(initialResult.ProofOfDeliveryId);
        var newPod = await context.ProofOfDeliveries.FindAsync(supersedeResult.ProofOfDeliveryId);

        Assert.NotNull(oldPod);
        Assert.NotNull(newPod);

        Assert.True(oldPod.IsSuperseded);
        Assert.Equal(newPod.Id, oldPod.SupersededByPodId);
        Assert.Equal(supersedeRequest.Reason, oldPod.SupersededReason);

        Assert.False(newPod.IsSuperseded);
        Assert.Equal("Correct Recipient", newPod.RecipientName);

        // 4. Query active POD returns newPod
        var activeDto = await service.GetProofOfDeliveryByPackageIdAsync(pkg.Id);
        Assert.NotNull(activeDto);
        Assert.Equal(newPod.Id, activeDto.Id);
        Assert.False(activeDto.IsSuperseded);

        // Query historical old POD returns oldPod with superseded metadata
        var oldDto = await service.GetProofOfDeliveryByIdAsync(oldPod.Id);
        Assert.NotNull(oldDto);
        Assert.True(oldDto.IsSuperseded);
        Assert.Equal(newPod.Id, oldDto.SupersededByPodId);
    }

    [Fact]
    public async Task ReconcilePendingStorage_ReconcilesStagedPods()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        // Manually insert an unfinalized POD older than 1 hour
        var unfinalizedPod = new ProofOfDelivery
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = pkg.CarrierCode!,
            EvidenceSource = PodEvidenceSource.CarrierApi,
            CarrierConfirmationNumber = "RECON-001",
            IsStorageFinalized = false,
            DeliveredAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        unfinalizedPod.CreatedAt = DateTime.UtcNow.AddHours(-2);
        context.ProofOfDeliveries.Add(unfinalizedPod);
        await context.SaveChangesAsync();

        int reconciled = await service.ReconcilePendingStorageAsync(TimeSpan.Zero);
        Assert.True(reconciled >= 1);

        var refreshedPod = await context.ProofOfDeliveries.FindAsync(unfinalizedPod.Id);
        Assert.NotNull(refreshedPod);
        Assert.True(refreshedPod.IsStorageFinalized);
    }

    [Fact]
    public async Task Invariant_WarehouseInventoryRemainsUnchanged_ZeroMutation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        // Record initial inventory state
        var initialStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        decimal initialTotal = initialStock.TotalQuantity;
        decimal initialAllocated = initialStock.QtyAllocated;
        decimal initialAvailable = initialStock.QtyAvailable;
        int initialTxCount = await context.InventoryTransactions.CountAsync();

        var service = new ProofOfDeliveryService(context, _storageService, tenantProvider, userContext, NullLogger<ProofOfDeliveryService>.Instance);

        var request = new SubmitProofOfDeliveryRequest
        {
            PackageId = pkg.Id,
            CarrierConfirmationNumber = "ZERO-MUT-CONF-001",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            RecipientName = "Inventory Auditor"
        };
        await service.SubmitProofOfDeliveryAsync(request);

        // Verify stock on hand and ledger underwent zero mutations (ΔQ = 0)
        var stockAfter = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, stockAfter.TotalQuantity);
        Assert.Equal(initialAllocated, stockAfter.QtyAllocated);
        Assert.Equal(initialAvailable, stockAfter.QtyAvailable);

        int txCountAfter = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, txCountAfter);
    }
}
