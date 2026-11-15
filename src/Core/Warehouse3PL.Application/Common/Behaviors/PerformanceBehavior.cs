using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Common.Behaviors;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly Stopwatch _timer;
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserContext _currentUser;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, ICurrentUserContext currentUser)
    {
        _timer = new Stopwatch();
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _timer.Restart();

        var response = await next();

        _timer.Stop();

        var elapsedMilliseconds = _timer.ElapsedMilliseconds;

        if (elapsedMilliseconds > 500)
        {
            var requestName = typeof(TRequest).Name;
            var userId = _currentUser.UserId?.ToString() ?? "Anonymous";
            var tenantId = _currentUser.TenantId?.ToString() ?? "NoTenant";

            _logger.LogWarning("Long running request detected: {RequestName} ({ElapsedMilliseconds} milliseconds) [User: {UserId}, Tenant: {TenantId}]",
                requestName, elapsedMilliseconds, userId, tenantId);
        }

        return response;
    }
}

