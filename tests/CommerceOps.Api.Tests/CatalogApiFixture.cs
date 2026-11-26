using System.Net.Http.Headers;
using System.Security.Cryptography;
using CommerceOps.Api.Auth;
using Microsoft.AspNetCore.Identity;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class CatalogApiFixture : IAsyncLifetime
{
    private readonly SqliteConnection? sqlite;
    private readonly string? sqlServer;
    private readonly WebApplicationFactory<Program> factory;
    private bool initialized;
    private readonly TestApiFactory testHost = new();
    private string adminToken = "";
    internal string AdminName { get; } = $"admin-{Guid.NewGuid():N}";
    internal string AdminPassword { get; } = GeneratePassword();
    internal byte[] SigningKey => testHost.SigningKey;

    internal static string GeneratePassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1!";

    public CatalogApiFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable("COMMERCEOPS_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            sqlite = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        }
        else
        {
            sqlServer = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = $"CommerceOpsTests_{Guid.NewGuid():N}"
            }.ConnectionString;
        }

        factory = testHost.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            if (sqlite is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<CatalogDbContext>>();
                    services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(sqlite));
                });
            }
            else
            {
                builder.UseSetting("ConnectionStrings:Catalog", sqlServer);
            }
        });
    }

    public bool UsesSqlServer => sqlServer is not null;

    public HttpClient CreateClient() => factory.CreateClient(TestApiFactory.HttpsOptions);

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return client;
    }
    public AsyncServiceScope CreateScope() => factory.Services.CreateAsyncScope();

    public async Task InitializeAsync()
    {
        if (sqlite is not null)
        {
            await sqlite.OpenAsync();
        }

        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (UsesSqlServer)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        initialized = true;
        var services = scope.ServiceProvider;
        await services.GetRequiredService<AdminBootstrap>().RunAsync(AdminName, AdminPassword);
        var users = services.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var admin = (await users.FindByNameAsync(AdminName))!;
        adminToken = services.GetRequiredService<AccessTokenIssuer>().Issue(admin, await users.GetRolesAsync(admin)).AccessToken;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (UsesSqlServer && initialized)
            {
                await using var scope = CreateScope();
                await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            await testHost.DisposeAsync();
            if (sqlite is not null)
            {
                await sqlite.DisposeAsync();
            }
        }
    }
}
