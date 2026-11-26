using System.Net;
using System.Net.Http.Json;
using CommerceOps.Api.Auth;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class LoginRateLimitTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    [Fact]
    public async Task Login_RejectsBurstAfterRateLimit()
    {
        using var client = fixture.CreateClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(null, null));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
