using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommerceOps.Api.Auth;

internal static class AuthRegistration
{
    public static void AddCommerceOpsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
        {
            options.Password.RequiredLength = 15;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<CatalogDbContext>();

        services.AddOptions<JwtSettings>().Bind(configuration.GetSection("Jwt"))
            .Validate(settings => settings.IsValid(),
                "Set Jwt:Issuer, Jwt:Audience, and Jwt:SigningKey (at least 32 random bytes in Base64).")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, settings) =>
            {
                var jwt = settings.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    RequireAudience = true,
                    ValidAudience = jwt.Audience,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal!;
                        var roles = principal.FindAll("role").ToArray();
                        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var id) || id == Guid.Empty ||
                            string.IsNullOrWhiteSpace(principal.Identity?.Name) || roles.Length != 1 ||
                            !AuthPolicies.Roles.Contains(roles[0].Value, StringComparer.Ordinal))
                        {
                            context.Fail("Invalid access token claims.");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole(AuthPolicies.Admin))
            .AddPolicy(AuthPolicies.InventoryWrite, policy => policy.RequireRole(AuthPolicies.Admin, AuthPolicies.Operator));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthPolicies.LoginLimit, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.MapToIPv6() ?? IPAddress.IPv6None,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
        });
        services.AddScoped<AccountService>();
        services.AddScoped<AdminBootstrap>();
        services.AddSingleton<AccessTokenIssuer>();
    }
}
