using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Cronus.DeliveryService.Errors;

/// <summary>
/// Turns domain exceptions into consistent RFC 9457 problem details responses. Unexpected
/// exceptions are logged in full and reported as an opaque 500 so internals are never leaked.
/// </summary>
internal sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            RequestValidationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

        ProblemDetails problem = exception is RequestValidationException validationException
            ? new ValidationProblemDetails(new Dictionary<string, string[]>(validationException.Errors))
            : new ProblemDetails();

        problem.Status = statusCode;
        problem.Title = ReasonPhrases.GetReasonPhrase(statusCode);
        problem.Detail = statusCode == StatusCodes.Status500InternalServerError
            ? "An unexpected error occurred while processing the request."
            : exception.Message;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }
}
