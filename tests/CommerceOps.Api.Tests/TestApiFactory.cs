using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CommerceOps.Api.Tests;

public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    internal const string Issuer = "commerceops-tests";
    internal const string Audience = "commerceops-tests-client";
    internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(64);

    internal static WebApplicationFactoryClientOptions HttpsOptions => new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = Convert.ToBase64String(SigningKey)
            }));
}
