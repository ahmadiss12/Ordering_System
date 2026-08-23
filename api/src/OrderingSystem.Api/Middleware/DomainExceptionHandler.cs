using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Api.Middleware;

/// <summary>
/// Turns domain failures into RFC 9457 ProblemDetails, in one place.
/// <para>
/// Centralising it means clients get one error shape instead of per-endpoint guesswork, and it is
/// the only spot that decides what is safe to expose — an unexpected exception becomes a plain
/// 500 rather than leaking a stack trace or a connection string.
/// </para>
/// </summary>
internal sealed partial class DomainExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    // Source-generated rather than a plain LogError call: this sits on the request path, and the
    // generator avoids boxing the arguments and formatting the message when the level is disabled.
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string path);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Validation failed"),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Not authenticated"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Only a DomainException's message was written to be read by a caller. Anything else
            // could carry internal detail, so it is replaced.
            Detail = exception is DomainException ? exception.Message : "Please try again later.",
            Instance = httpContext.Request.Path,
        };

        if (exception is ValidationFailedException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
