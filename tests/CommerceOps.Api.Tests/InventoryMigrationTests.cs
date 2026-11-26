using CommerceOps.Application.Inventory;
using CommerceOps.Infrastructure.Catalog;
using CommerceOps.Infrastructure.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class InventoryMigrationTests
{
    [SqlServerFact]
    public async Task Migrations_BackfillStockAndPreserveCatalogAndMovementsWhenAddingIdentity()
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("COMMERCEOPS_TEST_SQLSERVER")!)
        {
            InitialCatalog = $"CommerceOpsUpgrade_{Guid.NewGuid():N}"
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new CatalogDbContext(options);

        try
        {
            await db.GetService<IMigrator>().MigrateAsync("InitialCatalog");
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO Categories (Id, Name) VALUES ({categoryId}, {"Existing category"})""");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Products (Id, Sku, Name, UnitPrice, CategoryId, IsActive)
                VALUES ({productId}, {"EXISTING"}, {"Existing product"}, {1m}, {categoryId}, {true})
                """);

            await db.GetService<IMigrator>().MigrateAsync("AddInventory");

            var store = new EfInventoryStore(db);
            Assert.Equal(new StockBalance(productId, 0), await store.FindBalanceAsync(productId, default));
            Assert.Equal(0, await store.CountMovementsAsync(productId, default));
            var movement = new StockMovement(Guid.NewGuid(), productId, StockMovementType.Receipt, 3,
                DateTime.UtcNow, null, null);
            await store.ApplyMovementAsync(movement, default);

            await db.Database.MigrateAsync();

            Assert.Equal(new StockBalance(productId, 3), await store.FindBalanceAsync(productId, default));
            Assert.Equal(movement.Id, (await db.StockMovements.AsNoTracking().SingleAsync()).Id);
            Assert.Equal("EXISTING", (await db.Products.AsNoTracking().SingleAsync()).Sku);
            Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
