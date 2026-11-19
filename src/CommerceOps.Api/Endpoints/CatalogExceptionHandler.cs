using CommerceOps.Application.Catalog;
using Microsoft.AspNetCore.Diagnostics;

namespace CommerceOps.Api.Endpoints;

internal sealed class CatalogExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CatalogException catalog)
        {
            return false;
        }

        var result = catalog.Error switch
        {
            CatalogError.Validation => Results.ValidationProblem(catalog.Errors!),
            CatalogError.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: catalog.Message),
            CatalogError.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, detail: catalog.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };
        await result.ExecuteAsync(httpContext);
        return true;
    }
}
