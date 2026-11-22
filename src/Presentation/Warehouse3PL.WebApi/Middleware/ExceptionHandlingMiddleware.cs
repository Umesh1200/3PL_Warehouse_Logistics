using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.WebApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate _next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        this._next = _next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        var statusCode = HttpStatusCode.InternalServerError;
        var title = "An internal server error occurred.";
        var errorCode = "INTERNAL_SERVER_ERROR";
        IDictionary<string, string[]>? validationErrors = null;

        switch (exception)
        {
            case ValidationException valEx:
                statusCode = HttpStatusCode.BadRequest;
                title = "Validation failure";
                errorCode = valEx.Code ?? "VALIDATION_ERROR";
                validationErrors = valEx.Errors;
                break;

            case UnauthorizedException unauthEx:
                statusCode = HttpStatusCode.Unauthorized;
                title = "Unauthorized";
                errorCode = unauthEx.Code ?? "UNAUTHORIZED";
                break;

            case ForbiddenException forbEx:
                statusCode = HttpStatusCode.Forbidden;
                title = "Access forbidden";
                errorCode = forbEx.Code ?? "FORBIDDEN";
                break;

            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Forbidden;
                title = "Access forbidden";
                errorCode = "FORBIDDEN";
                break;

            case EntityNotFoundException notFoundEx:
                statusCode = HttpStatusCode.NotFound;
                title = "Resource not found";
                errorCode = notFoundEx.Code ?? "NOT_FOUND";
                break;

            case ConcurrencyException concEx:
                statusCode = HttpStatusCode.Conflict;
                title = "Concurrency conflict";
                errorCode = concEx.Code ?? "CONCURRENCY_CONFLICT";
                break;

            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                statusCode = HttpStatusCode.Conflict;
                title = "Concurrency conflict";
                errorCode = "CONCURRENCY_CONFLICT";
                break;

            case DomainException domainEx:
                statusCode = HttpStatusCode.UnprocessableEntity;
                title = "Domain rule violation";
                errorCode = domainEx.Code ?? "DOMAIN_RULE_VIOLATION";
                break;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = traceId;

        if (validationErrors != null)
        {
            problemDetails.Extensions["errors"] = validationErrors;
        }

        if (_env.IsDevelopment() && statusCode == HttpStatusCode.InternalServerError)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }
}

