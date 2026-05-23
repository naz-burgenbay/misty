using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Common.Exceptions;

namespace Misty.Api.Common;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails)
        => _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        int status;
        string title;
        Dictionary<string, object?>? extensions = null;

        switch (ex)
        {
            case ValidationException vex:
                status = StatusCodes.Status422UnprocessableEntity;
                title = "One or more validation errors occurred.";
                extensions = new Dictionary<string, object?>
                {
                    ["errors"] = vex.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(
                            g => g.Key,
                            g => (object?)g.Select(e => e.ErrorMessage).ToArray())
                };
                break;

            case ConflictException:
                status = StatusCodes.Status409Conflict;
                title = ex.Message;
                break;

            case UnauthorizedException:
                status = StatusCodes.Status401Unauthorized;
                title = "Invalid credentials.";
                break;

            case ConcurrencyException:
                status = StatusCodes.Status409Conflict;
                title = ex.Message;
                break;

            default:
                return false; // Will change in the future
        }

        ctx.Response.StatusCode = status;

        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
        };

        if (extensions is not null)
            foreach (var (key, value) in extensions)
                pd.Extensions[key] = value;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            ProblemDetails = pd,
            Exception = ex,
        });
    }
}
