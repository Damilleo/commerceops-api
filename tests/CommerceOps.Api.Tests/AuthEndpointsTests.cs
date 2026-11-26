using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CommerceOps.Api.Auth;
using CommerceOps.Application.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class AuthEndpointsTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    [Fact]
    public async Task Login_ReturnsShortLivedBearerTokenAndMeReturnsIdentity()
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(fixture.AdminName, fixture.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(900, token.ExpiresIn);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal("Admin", jwt.Claims.Single(claim => claim.Type == "role").Value);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var identity = (await client.GetFromJsonAsync<AccountResponse>("/api/v1/auth/me"))!;
        Assert.NotEqual(Guid.Empty, identity.Id);
        Assert.Equal(fixture.AdminName, identity.UserName);
        Assert.Equal(["Admin"], identity.Roles);
    }

    [Fact]
    public async Task Login_UnknownWrongAndLockedCredentialsHaveTheSameResponse()
    {
        using var admin = fixture.CreateAdminClient();
        var (user, password) = await CreateUserAsync(admin, "Viewer");
        using var client = fixture.CreateClient();

        async Task<string?> TryLoginAsync(string name, string candidate)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(name, candidate));
            var problem = await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
            return problem.GetProperty("detail").GetString();
        }

        var wrong = CatalogApiFixture.GeneratePassword();
        var genericError = await TryLoginAsync($"unknown-{Guid.NewGuid():N}", wrong);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(genericError, await TryLoginAsync(user.UserName, wrong));
        }

        Assert.Equal(genericError, await TryLoginAsync(user.UserName, password));
    }

    [Fact]
    public async Task Bootstrap_CanRunAgainWithoutCreatingAnotherAdministrator()
    {
        await using var scope = fixture.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
        Assert.False(await bootstrap.RunAsync("replacement", CatalogApiFixture.GeneratePassword()));
    }

    [Theory]
    [InlineData("POST", "/api/v1/users")]
    [InlineData("POST", "/api/v1/categories")]
    [InlineData("PUT", "/api/v1/categories/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/v1/categories/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/products")]
    [InlineData("PUT", "/api/v1/products/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/v1/products/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/inventory/11111111-1111-1111-1111-111111111111/receipts")]
    [InlineData("POST", "/api/v1/inventory/11111111-1111-1111-1111-111111111111/issues")]
    [InlineData("GET", "/api/v1/auth/me")]
    public async Task ProtectedRoutes_RejectAnonymousRequests(string method, string path)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
    }

    [Fact]
    public async Task ReadsAreAnonymousAndWritePermissionsFollowRoleMatrix()
    {
        using var admin = fixture.CreateAdminClient();
        var (operatorUser, operatorPassword) = await CreateUserAsync(admin, "Operator");
        var (viewer, viewerPassword) = await CreateUserAsync(admin, "Viewer");
        using var operatorClient = await LoginClientAsync(operatorUser.UserName, operatorPassword);
        using var viewerClient = await LoginClientAsync(viewer.UserName, viewerPassword);
        using var anonymous = fixture.CreateClient();

        using var categoryResponse = await admin.PostAsJsonAsync("/api/v1/categories", new CategoryRequest("Role test", null));
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<Category>())!;
        using var productResponse = await admin.PostAsJsonAsync("/api/v1/products",
            new ProductRequest(Guid.NewGuid().ToString("N"), "Role product", null, 1m, category.Id));
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = (await productResponse.Content.ReadFromJsonAsync<Product>())!;
        var inventory = $"/api/v1/inventory/{product.Id}";

        foreach (var route in new[] { "/health", "/api/v1/categories", $"/api/v1/categories/{category.Id}",
                     "/api/v1/products", $"/api/v1/products/{product.Id}", inventory, $"{inventory}/movements" })
        {
            using var read = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using var operatorReceipt = await operatorClient.PostAsJsonAsync($"{inventory}/receipts", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, operatorReceipt.StatusCode);
        using var operatorIssue = await operatorClient.PostAsJsonAsync($"{inventory}/issues", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, operatorIssue.StatusCode);
        using var viewerReceipt = await viewerClient.PostAsJsonAsync($"{inventory}/receipts", new { quantity = 1 });
        await AssertProblemAsync(viewerReceipt, HttpStatusCode.Forbidden);
        using var viewerIssue = await viewerClient.PostAsJsonAsync($"{inventory}/issues", new { quantity = 1 });
        await AssertProblemAsync(viewerIssue, HttpStatusCode.Forbidden);

        foreach (var client in new[] { operatorClient, viewerClient })
        {
            using var createCategory = await client.PostAsJsonAsync("/api/v1/categories", new CategoryRequest("Blocked", null));
            await AssertProblemAsync(createCategory, HttpStatusCode.Forbidden);
            using var deleteProduct = await client.DeleteAsync($"/api/v1/products/{product.Id}");
            await AssertProblemAsync(deleteProduct, HttpStatusCode.Forbidden);
            using var createUser = await client.PostAsJsonAsync("/api/v1/users",
                new CreateUserRequest("blocked", CatalogApiFixture.GeneratePassword(), "Viewer"));
            await AssertProblemAsync(createUser, HttpStatusCode.Forbidden);
        }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-algorithm")]
    [InlineData("unknown-role")]
    public async Task Me_RejectsInvalidTokens(string defect)
    {
        using var client = fixture.CreateClient();
        var now = DateTime.UtcNow;
        var key = defect == "wrong-key" ? RandomNumberGenerator.GetBytes(64) : fixture.SigningKey;
        var jwt = new JwtSecurityToken(
            defect == "wrong-issuer" ? "other-issuer" : TestApiFactory.Issuer,
            defect == "wrong-audience" ? "other-audience" : TestApiFactory.Audience,
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("name", "test"),
             new Claim("role", defect == "unknown-role" ? "Unsupported" : "Admin")],
            now.AddMinutes(-20), defect == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(key),
                defect == "wrong-algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        using var response = await client.GetAsync("/api/v1/auth/me");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task OpenApi_DeclaresBearerForProtectedRoutes()
    {
        using var client = fixture.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("bearer", schemes.GetProperty("bearer").GetProperty("scheme").GetString());
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/users").GetProperty("post")
            .GetProperty("security")[0].TryGetProperty("bearer", out _));
        Assert.False(paths.GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/v1/products").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task AdminUserCreation_ValidatesRolePasswordAndDuplicateName()
    {
        using var admin = fixture.CreateAdminClient();
        var (user, password) = await CreateUserAsync(admin, "Viewer");

        using var duplicate = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest(user.UserName.ToUpperInvariant(), password, "Viewer"));
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
        using var invalidRole = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("invalid-role", password, "Superuser"));
        await AssertProblemAsync(invalidRole, HttpStatusCode.BadRequest);
        using var weakPassword = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("weak-password", "abc", "Viewer"));
        await AssertProblemAsync(weakPassword, HttpStatusCode.BadRequest);
    }

    private static async Task<(AccountResponse User, string Password)> CreateUserAsync(HttpClient admin, string role)
    {
        var password = CatalogApiFixture.GeneratePassword();
        using var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest($"user-{Guid.NewGuid():N}", password, role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("password", out _));
        Assert.False(body.TryGetProperty("passwordHash", out _));
        var account = body.Deserialize<AccountResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal([role], account.Roles);
        return (account, password);
    }

    private async Task<HttpClient> LoginClientAsync(string userName, string password)
    {
        var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
