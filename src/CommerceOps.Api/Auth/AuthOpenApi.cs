using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CommerceOps.Api.Auth;

internal static class AuthOpenApi
{
    private const string Scheme = "bearer";

    public static void AddBearerDocumentation(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = Scheme,
                BearerFormat = "JWT"
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Scheme, context.Document)] = [] }
                ];
            }

            return Task.CompletedTask;
        });
    }
}
