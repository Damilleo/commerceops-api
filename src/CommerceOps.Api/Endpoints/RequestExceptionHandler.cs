using CommerceOps.Application.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace CommerceOps.Api.Endpoints;

internal sealed class RequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RequestException request)
        {
            return false;
        }

        var result = request.Error switch
        {
            RequestError.Validation => Results.ValidationProblem(request.Errors!),
            RequestError.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: request.Message),
            RequestError.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: request.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };
        await result.ExecuteAsync(httpContext);
        return true;
    }
}
