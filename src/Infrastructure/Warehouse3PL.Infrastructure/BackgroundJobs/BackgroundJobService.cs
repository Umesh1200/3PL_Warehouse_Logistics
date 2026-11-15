using System.Linq.Expressions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.BackgroundJobs;

public class BackgroundJobService : IBackgroundJobService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackgroundJobService> _logger;
    private readonly bool _hangfireEnabled;

    // In-memory queue for testing or when Hangfire is disabled
    private static readonly List<string> TestExecutedJobs = [];
    public static IReadOnlyList<string> ExecutedJobs => TestExecutedJobs.AsReadOnly();
    public static void ClearTestJobs() => TestExecutedJobs.Clear();

    public BackgroundJobService(IConfiguration configuration, ILogger<BackgroundJobService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _hangfireEnabled = configuration.GetValue<bool>("Hangfire:Enabled", false);
    }

    public string Enqueue(Expression<Action> methodCall)
    {
        if (_hangfireEnabled)
        {
            return BackgroundJob.Enqueue(methodCall);
        }

        var jobId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Simulating immediate job execution for job {JobId}: {Method}", jobId, methodCall.Body);
        TestExecutedJobs.Add(jobId);
        
        try
        {
            methodCall.Compile().Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing simulated background job {JobId}", jobId);
        }

        return jobId;
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        if (_hangfireEnabled)
        {
            return BackgroundJob.Enqueue<T>(methodCall);
        }

        var jobId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Simulating generic job execution for job {JobId}: {Method}", jobId, methodCall.Body);
        TestExecutedJobs.Add(jobId);
        return jobId;
    }

    public string Schedule(Expression<Action> methodCall, TimeSpan delay)
    {
        if (_hangfireEnabled)
        {
            return BackgroundJob.Schedule(methodCall, delay);
        }

        var jobId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Simulating scheduled job {JobId} with delay {Delay}: {Method}", jobId, delay, methodCall.Body);
        TestExecutedJobs.Add(jobId);
        return jobId;
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        if (_hangfireEnabled)
        {
            return BackgroundJob.Schedule<T>(methodCall, delay);
        }

        var jobId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Simulating generic scheduled job {JobId} with delay {Delay}: {Method}", jobId, delay, methodCall.Body);
        TestExecutedJobs.Add(jobId);
        return jobId;
    }

    public void AddOrUpdateRecurringJob(string recurringJobId, Expression<Action> methodCall, string cronExpression)
    {
        if (_hangfireEnabled)
        {
            RecurringJob.AddOrUpdate(recurringJobId, methodCall, cronExpression);
            return;
        }

        _logger.LogInformation("Simulating recurring job registration {RecurringJobId} [{Cron}]: {Method}",
            recurringJobId, cronExpression, methodCall.Body);
        TestExecutedJobs.Add(recurringJobId);
    }

    public void AddOrUpdateRecurringJob<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression)
    {
        if (_hangfireEnabled)
        {
            RecurringJob.AddOrUpdate<T>(recurringJobId, methodCall, cronExpression);
            return;
        }

        _logger.LogInformation("Simulating generic recurring job registration {RecurringJobId} [{Cron}]: {Method}",
            recurringJobId, cronExpression, methodCall.Body);
        TestExecutedJobs.Add(recurringJobId);
    }
}

