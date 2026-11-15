using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Xunit;

namespace Warehouse3PL.UnitTests.BackgroundJobs;

public class BackgroundJobVerificationTests
{
    public class SampleWarehouseJobs
    {
        public static bool JobExecuted { get; set; }
        public static string? ProcessedOrderNumber { get; set; }

        public static void ProcessInventoryReconciliation()
        {
            JobExecuted = true;
        }

        public static void GenerateClientInvoice(string orderNumber)
        {
            ProcessedOrderNumber = orderNumber;
        }
    }

    [Fact]
    public void Enqueue_ShouldExecuteJobImmediately_WhenHangfireIsSimulated()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Hangfire:Enabled", "false" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var jobService = new BackgroundJobService(configuration, NullLogger<BackgroundJobService>.Instance);
        SampleWarehouseJobs.JobExecuted = false;

        // Act
        var jobId = jobService.Enqueue(() => SampleWarehouseJobs.ProcessInventoryReconciliation());

        // Assert
        Assert.NotNull(jobId);
        Assert.NotEmpty(jobId);
        Assert.True(SampleWarehouseJobs.JobExecuted);
        Assert.Contains(jobId, BackgroundJobService.ExecutedJobs);
    }

    [Fact]
    public void Schedule_ShouldRegisterJobId()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Hangfire:Enabled", "false" } })
            .Build();

        var jobService = new BackgroundJobService(configuration, NullLogger<BackgroundJobService>.Instance);

        // Act
        var jobId = jobService.Schedule(
            () => SampleWarehouseJobs.ProcessInventoryReconciliation(),
            TimeSpan.FromHours(1));

        // Assert
        Assert.NotNull(jobId);
        Assert.Contains(jobId, BackgroundJobService.ExecutedJobs);
    }

    [Fact]
    public void AddOrUpdateRecurringJob_ShouldRegisterJobSchedule()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Hangfire:Enabled", "false" } })
            .Build();

        var jobService = new BackgroundJobService(configuration, NullLogger<BackgroundJobService>.Instance);
        const string jobId = "daily-inventory-cycle-count";

        // Act
        jobService.AddOrUpdateRecurringJob(
            jobId,
            () => SampleWarehouseJobs.ProcessInventoryReconciliation(),
            "0 2 * * *"); // 2:00 AM daily

        // Assert
        Assert.Contains(jobId, BackgroundJobService.ExecutedJobs);
    }
}

