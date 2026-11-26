using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CommerceOps.Api.Auth;

namespace CommerceOps.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/login", async (LoginRequest request, AccountService accounts, HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var token = await accounts.LoginAsync(request);
                return token is not null
                    ? Results.Ok(token)
                    : Results.Problem(statusCode: 401, detail: "Invalid username or password.");
            })
            .AllowAnonymous().RequireRateLimiting(AuthPolicies.LoginLimit)
            .WithSummary("Exchange credentials for a 15-minute bearer access token")
            .Produces<TokenResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);

        auth.MapGet("/me", (ClaimsPrincipal user, HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(new AccountResponse(
                    Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!), user.Identity!.Name!,
                    user.FindAll("role").Select(claim => claim.Value).ToArray()));
            })
            .RequireAuthorization().WithSummary("Get the authenticated identity and token roles")
            .ProducesProblem(401);

        api.MapPost("/users", async (CreateUserRequest request, AccountService accounts) =>
                Results.Json(await accounts.CreateAsync(request), statusCode: 201))
            .RequireAuthorization(AuthPolicies.Admin).WithTags("Users").WithSummary("Create a user with one role")
            .Produces<AccountResponse>(201).ProducesValidationProblem()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
    }
}
