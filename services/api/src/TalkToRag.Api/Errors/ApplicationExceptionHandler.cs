using Microsoft.AspNetCore.Diagnostics;
using TalkToRag.Application.Common.Exceptions;

namespace TalkToRag.Api.Errors;

/// <summary>Turns the application's exceptions into standard problem+json responses with the right status code.</summary>
public sealed class ApplicationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            RequestValidationException => StatusCodes.Status400BadRequest,
            AuthenticationFailedException => StatusCodes.Status401Unauthorized,
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            _ => 0,
        };
        if (status == 0)
            return false; // Unexpected: let the default handler return a 500 without details.

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = exception.Message },
        });
    }
}
