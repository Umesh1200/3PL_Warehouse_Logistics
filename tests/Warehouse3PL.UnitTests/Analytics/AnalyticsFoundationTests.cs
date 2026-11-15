using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Unit tests verifying Phase 6.0.1: Analytics Architecture, Schemas &amp; Checkpoint Invariants.
/// </summary>
public class AnalyticsFoundationTests
{
    [Fact]
    public void Test_01_ProjectionCheckpoint_Initializes_Correctly()
    {
        var checkpoint = ProjectionCheckpoint.Create("DailyFacilityThroughputSummary", 2);

        Assert.Equal("DailyFacilityThroughputSummary", checkpoint.ProjectionName);
        Assert.Equal(2, checkpoint.Version);
        Assert.Equal(ProjectionStatus.Active, checkpoint.Status);
        Assert.Equal(0, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(DateTime.UnixEpoch, checkpoint.LastProcessedTimestampUtc);
        Assert.Equal(0, checkpoint.ProcessedCount);
        Assert.Null(checkpoint.ErrorMessage);
    }

    [Fact]
    public void Test_02_ProjectionCheckpoint_Advances_Watermark()
    {
        var checkpoint = ProjectionCheckpoint.Create("ClientProfitabilitySummary");
        var eventId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        checkpoint.UpdateWatermark(eventId, 100, timestamp);

        Assert.Equal(eventId, checkpoint.LastProcessedEventId);
        Assert.Equal(100, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(timestamp, checkpoint.LastProcessedTimestampUtc);
        Assert.Equal(1, checkpoint.ProcessedCount);
    }

    [Fact]
    public void Test_03_ProjectionCheckpoint_Prevents_Backward_Watermark_When_Active()
    {
        var checkpoint = ProjectionCheckpoint.Create("InventoryUtilizationSummary");
        checkpoint.UpdateWatermark(Guid.NewGuid(), 500, DateTime.UtcNow);

        // Attempting to move backward when Active must throw
        var ex = Assert.Throws<InvalidDomainStateException>(() =>
            checkpoint.UpdateWatermark(Guid.NewGuid(), 400, DateTime.UtcNow));

        Assert.Contains("backward", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test_04_ProjectionCheckpoint_Allows_Backward_Watermark_When_Replaying()
    {
        var checkpoint = ProjectionCheckpoint.Create("InventoryUtilizationSummary");
        checkpoint.UpdateWatermark(Guid.NewGuid(), 500, DateTime.UtcNow);

        checkpoint.MarkReplaying();
        Assert.Equal(ProjectionStatus.Replaying, checkpoint.Status);

        // Can move backward when Replaying
        var newEventId = Guid.NewGuid();
        checkpoint.UpdateWatermark(newEventId, 100, DateTime.UtcNow);

        Assert.Equal(100, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(newEventId, checkpoint.LastProcessedEventId);
    }

    [Fact]
    public void Test_05_ProjectionCheckpoint_Lifecycle_Status_Transitions()
    {
        var checkpoint = ProjectionCheckpoint.Create("LaborProductivitySummary");

        checkpoint.Pause();
        Assert.Equal(ProjectionStatus.Paused, checkpoint.Status);

        checkpoint.Fail("Database connection timed out during projection rebuild");
        Assert.Equal(ProjectionStatus.Failed, checkpoint.Status);
        Assert.Equal("Database connection timed out during projection rebuild", checkpoint.ErrorMessage);

        checkpoint.MarkActive();
        Assert.Equal(ProjectionStatus.Active, checkpoint.Status);
        Assert.Null(checkpoint.ErrorMessage);
    }

    [Fact]
    public void Test_06_ProjectionCheckpoint_ResetToBeginning_Resets_Watermark()
    {
        var checkpoint = ProjectionCheckpoint.Create("CarrierSlaPerformanceSummary");
        checkpoint.UpdateWatermark(Guid.NewGuid(), 9999, DateTime.UtcNow);

        checkpoint.ResetToBeginning();

        Assert.Equal(ProjectionStatus.Replaying, checkpoint.Status);
        Assert.Null(checkpoint.LastProcessedEventId);
        Assert.Equal(0, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(DateTime.UnixEpoch, checkpoint.LastProcessedTimestampUtc);
        Assert.Equal(0, checkpoint.ProcessedCount);
    }

    [Fact]
    public void Test_07_AnalyticsEventDeduplicationRecord_Validates_Inputs()
    {
        var eventId = Guid.NewGuid();
        var dedup = AnalyticsEventDeduplicationRecord.Create("ThroughputProjection", eventId, "OrderDispatchedEvent");

        Assert.Equal("ThroughputProjection", dedup.ProjectionName);
        Assert.Equal(eventId, dedup.EventId);
        Assert.Equal("OrderDispatchedEvent", dedup.EventName);
        Assert.True(dedup.ProcessedAtUtc <= DateTime.UtcNow);

        Assert.Throws<ValidationException>(() =>
            AnalyticsEventDeduplicationRecord.Create("", eventId, "Event"));

        Assert.Throws<ValidationException>(() =>
            AnalyticsEventDeduplicationRecord.Create("Proj", Guid.Empty, "Event"));

        Assert.Throws<ValidationException>(() =>
            AnalyticsEventDeduplicationRecord.Create("Proj", eventId, ""));
    }

    [Fact]
    public void Test_08_Enums_Contain_All_Required_Analytics_Values()
    {
        Assert.True(Enum.IsDefined(typeof(TimeGrain), TimeGrain.Hourly));
        Assert.True(Enum.IsDefined(typeof(TimeGrain), TimeGrain.Daily));
        Assert.True(Enum.IsDefined(typeof(TimeGrain), TimeGrain.Weekly));
        Assert.True(Enum.IsDefined(typeof(TimeGrain), TimeGrain.Monthly));

        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.FulfillmentThroughput));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.LaborProductivity));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.InventoryUtilization));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.CarrierPerformance));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.FinancialProfitability));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.ReverseLogistics));
        Assert.True(Enum.IsDefined(typeof(AnalyticsMetricCategory), AnalyticsMetricCategory.DockAndYard));
    }
}

