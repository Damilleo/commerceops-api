using CommerceOps.Api.Commands;
using CommerceOps.Infrastructure.Catalog;
using CommerceOps.Infrastructure.Demo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class DemoSeedTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    [Fact]
    public async Task Seed_CanRunTwiceWithoutChangingExistingData()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var userCount = await db.Users.CountAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoCatalogSeeder>();
        Assert.Equal((2, 3), await seeder.SeedAsync());

        var existing = await db.Products.SingleAsync(product => product.Sku == "DEMO-BOX-12");
        existing.Name = "Locally customized shipping box";
        await db.SaveChangesAsync();

        Assert.Equal((0, 0), await seeder.SeedAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Categories.CountAsync());
        Assert.Equal(new[] { "DEMO-BIN-20", "DEMO-BOX-12", "DEMO-TAPE-48" },
            await db.Products.OrderBy(product => product.Sku).Select(product => product.Sku).ToArrayAsync());
        Assert.Equal("Locally customized shipping box",
            (await db.Products.SingleAsync(product => product.Id == existing.Id)).Name);
        Assert.Equal(userCount, await db.Users.CountAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.All(await db.Products.Select(product => EF.Property<int>(product, "StockQuantity")).ToListAsync(),
            quantity => Assert.Equal(0, quantity));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Command_RejectsOtherEnvironmentsWithoutResolvingPersistence(string environment)
    {
        var requestedPersistence = false;
        using var services = new ServiceCollection().AddScoped<DemoCatalogSeeder>(_ =>
        {
            requestedPersistence = true;
            throw new InvalidOperationException("Database access was attempted.");
        }).BuildServiceProvider();
        Assert.Equal(1, await SeedDemoCommand.RunAsync(new TestEnvironment { EnvironmentName = environment }, services));
        Assert.False(requestedPersistence);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
