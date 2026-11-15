using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class ProofOfDeliveryDomainTests
{
    [Fact]
    public void Validate_ValidCoordinates_Passes()
    {
        var pod = new ProofOfDelivery
        {
            Latitude = 41.8781m,
            Longitude = -87.6298m,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-10),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex = Record.Exception(() => pod.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(90.000001)]
    [InlineData(-90.000001)]
    [InlineData(100.5)]
    [InlineData(-120.0)]
    public void Validate_LatitudeOutOfRange_ThrowsValidationException(decimal invalidLat)
    {
        var pod = new ProofOfDelivery
        {
            Latitude = invalidLat,
            Longitude = -87.6298m,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex = Assert.Throws<ValidationException>(() => pod.Validate());
        Assert.Contains("Latitude", ex.Message);
    }

    [Theory]
    [InlineData(180.000001)]
    [InlineData(-180.000001)]
    [InlineData(200.0)]
    [InlineData(-195.5)]
    public void Validate_LongitudeOutOfRange_ThrowsValidationException(decimal invalidLon)
    {
        var pod = new ProofOfDelivery
        {
            Latitude = 41.8781m,
            Longitude = invalidLon,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex = Assert.Throws<ValidationException>(() => pod.Validate());
        Assert.Contains("Longitude", ex.Message);
    }

    [Fact]
    public void Validate_IncompleteCoordinates_ThrowsValidationException()
    {
        var pod1 = new ProofOfDelivery
        {
            Latitude = 41.8781m,
            Longitude = null,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex1 = Assert.Throws<ValidationException>(() => pod1.Validate());
        Assert.Contains("Longitude", ex1.Message);

        var pod2 = new ProofOfDelivery
        {
            Latitude = null,
            Longitude = -87.6298m,
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex2 = Assert.Throws<ValidationException>(() => pod2.Validate());
        Assert.Contains("Latitude", ex2.Message);
    }

    [Fact]
    public void Validate_FutureDeliveryTimestamp_ThrowsValidationException()
    {
        var pod = new ProofOfDelivery
        {
            DeliveredAtUtc = DateTime.UtcNow.AddDays(1),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var ex = Assert.Throws<ValidationException>(() => pod.Validate());
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DriverHandheldMissingEvidence_ThrowsValidationException()
    {
        var pod = new ProofOfDelivery
        {
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-10),
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            IsSigned = false,
            SignatureObjectKey = null,
            PhotoObjectKey = null,
            RecipientName = null
        };

        var ex = Assert.Throws<ValidationException>(() => pod.Validate());
        Assert.Contains("Driver handheld delivery confirmation requires", ex.Message);
    }

    [Fact]
    public void Validate_DriverHandheldWithSignature_Passes()
    {
        var pod = new ProofOfDelivery
        {
            DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-10),
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            IsSigned = true,
            SignatureObjectKey = "tenants/tenant1/pod/pkg1/sig.png",
            RecipientName = "John Doe"
        };

        var ex = Record.Exception(() => pod.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void IsIdempotentDuplicate_MatchesCarrierConfirmationNumber()
    {
        var pod = new ProofOfDelivery
        {
            CarrierConfirmationNumber = "FDX-CONF-12345",
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        Assert.True(pod.IsIdempotentDuplicate("FDX-CONF-12345", null, null, PodEvidenceSource.CarrierApi));
        Assert.True(pod.IsIdempotentDuplicate("  fdx-conf-12345  ", null, null, PodEvidenceSource.CarrierApi));
        Assert.False(pod.IsIdempotentDuplicate("FDX-CONF-DIFFERENT", null, null, PodEvidenceSource.CarrierApi));
        Assert.False(pod.IsIdempotentDuplicate("FDX-CONF-12345", null, null, PodEvidenceSource.DriverHandheld));
    }

    [Fact]
    public void IsIdempotentDuplicate_MatchesCarrierEventId()
    {
        var pod = new ProofOfDelivery
        {
            CarrierEventId = "EVT-7890",
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        Assert.True(pod.IsIdempotentDuplicate(null, "EVT-7890", null, PodEvidenceSource.CarrierApi));
        Assert.True(pod.IsIdempotentDuplicate(null, " evt-7890 ", null, PodEvidenceSource.CarrierApi));
        Assert.False(pod.IsIdempotentDuplicate(null, "EVT-OTHER", null, PodEvidenceSource.CarrierApi));
    }

    [Fact]
    public void IsIdempotentDuplicate_MatchesIdempotencyKey()
    {
        var pod = new ProofOfDelivery
        {
            IdempotencyKey = "IDEMP-KEY-9999",
            EvidenceSource = PodEvidenceSource.DriverHandheld
        };

        Assert.True(pod.IsIdempotentDuplicate(null, null, "IDEMP-KEY-9999", PodEvidenceSource.DriverHandheld));
        Assert.False(pod.IsIdempotentDuplicate(null, null, "IDEMP-KEY-OTHER", PodEvidenceSource.DriverHandheld));
    }

    [Fact]
    public void HasConflictWith_DetectsContradictoryData()
    {
        var pod = new ProofOfDelivery
        {
            RecipientName = "Alice Smith",
            CarrierConfirmationNumber = "CONF-1111",
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        // Same data -> no conflict
        Assert.False(pod.HasConflictWith("Alice Smith", "CONF-1111", PodEvidenceSource.CarrierApi));

        // Different carrier confirmation -> conflict
        Assert.True(pod.HasConflictWith("Alice Smith", "CONF-9999", PodEvidenceSource.CarrierApi));

        // Different recipient name -> conflict
        Assert.True(pod.HasConflictWith("Bob Jones", "CONF-1111", PodEvidenceSource.CarrierApi));
    }

    [Fact]
    public void Supersede_MarksSupersededAndAuditsReason()
    {
        var pod = new ProofOfDelivery
        {
            DeliveredAtUtc = DateTime.UtcNow.AddHours(-1),
            EvidenceSource = PodEvidenceSource.CarrierApi
        };

        var newPodId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        pod.Supersede(newPodId, "Corrected delivery address proof uploaded by staff", now);

        Assert.True(pod.IsSuperseded);
        Assert.Equal(newPodId, pod.SupersededByPodId);
        Assert.Equal("Corrected delivery address proof uploaded by staff", pod.SupersededReason);
        Assert.Equal(now, pod.SupersededAtUtc);

        // Attempting to supersede again throws InvalidDomainStateException
        Assert.Throws<InvalidDomainStateException>(() =>
            pod.Supersede(Guid.NewGuid(), "Second supersession attempt", DateTime.UtcNow));
    }

    [Fact]
    public void ProofOfDeliveryConfiguration_ModelMetadata_VerifiesIndexesAndConstraints()
    {
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            Guid.NewGuid().ToString());

        var entityType = context.Model.FindEntityType(typeof(ProofOfDelivery));
        Assert.NotNull(entityType);

        // Verify table name
        Assert.Equal("proof_of_deliveries", entityType.GetTableName());

        // 1. PackageId Unique Index
        var packageIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(ProofOfDelivery.PackageId)));
        Assert.NotNull(packageIndex);
        Assert.True(packageIndex.IsUnique);
        Assert.Equal("ix_proof_of_deliveries_package_unique", packageIndex.GetDatabaseName());

        // 2. ClientId + PackageId + IdempotencyKey Index
        var idempotencyIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "ix_proof_of_deliveries_client_package_idempotency");
        Assert.NotNull(idempotencyIndex);
        var idempPropNames = idempotencyIndex.Properties.Select(p => p.Name).ToList();
        Assert.Contains(nameof(ProofOfDelivery.ClientId), idempPropNames);
        Assert.Contains(nameof(ProofOfDelivery.PackageId), idempPropNames);
        Assert.Contains(nameof(ProofOfDelivery.IdempotencyKey), idempPropNames);

        // 3. ClientId + CarrierConfirmationNumber Index
        var carrierConfIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "ix_proof_of_deliveries_client_carrier_conf");
        Assert.NotNull(carrierConfIndex);
        var carrierConfPropNames = carrierConfIndex.Properties.Select(p => p.Name).ToList();
        Assert.Contains(nameof(ProofOfDelivery.ClientId), carrierConfPropNames);
        Assert.Contains(nameof(ProofOfDelivery.CarrierConfirmationNumber), carrierConfPropNames);
    }
}

