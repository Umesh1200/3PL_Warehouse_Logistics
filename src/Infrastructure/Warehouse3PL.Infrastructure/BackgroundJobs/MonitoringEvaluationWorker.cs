using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Infrastructure.Services.Analytics;
namespace Warehouse3PL.Infrastructure.BackgroundJobs;
public sealed class MonitoringEvaluationWorker(IServiceScopeFactory scopes,ILogger<MonitoringEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope=scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<MonitoringService>().EvaluateScheduledAsync(stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Monitoring evaluation failed; no operational action was taken.");}
        }
    }
}
