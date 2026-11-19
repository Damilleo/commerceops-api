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

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

    public HttpClient CreateClient() => factory.CreateClient();
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
            await factory.DisposeAsync();
            if (sqlite is not null)
            {
                await sqlite.DisposeAsync();
            }
        }
    }
}
