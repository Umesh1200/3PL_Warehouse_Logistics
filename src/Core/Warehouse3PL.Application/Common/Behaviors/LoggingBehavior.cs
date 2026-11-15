using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserContext _currentUser;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUserContext currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUser.UserId?.ToString() ?? "Anonymous";
        var tenantId = _currentUser.TenantId?.ToString() ?? "NoTenant";

        _logger.LogInformation("Handling {RequestName} [User: {UserId}, Tenant: {TenantId}]",
            requestName, userId, tenantId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.LogInformation("Handled {RequestName} successfully in {ElapsedMilliseconds}ms [User: {UserId}, Tenant: {TenantId}]",
                requestName, stopwatch.ElapsedMilliseconds, userId, tenantId);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error handling {RequestName} after {ElapsedMilliseconds}ms [User: {UserId}, Tenant: {TenantId}]",
                requestName, stopwatch.ElapsedMilliseconds, userId, tenantId);
            throw;
        }
    }
}

