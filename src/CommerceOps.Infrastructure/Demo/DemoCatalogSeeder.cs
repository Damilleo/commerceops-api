using System.Data;
using CommerceOps.Application.Catalog;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Demo;

public sealed class DemoCatalogSeeder(CatalogDbContext db)
{
    private static readonly Guid Shipping = new("c01d0000-0000-4000-8000-000000000001");
    private static readonly Guid Warehouse = new("c01d0000-0000-4000-8000-000000000002");

    public async Task<(int Categories, int Products)> SeedAsync(CancellationToken cancellationToken = default)
    {
        // Serialize the existence checks and inserts so concurrent runs cannot partially seed the catalog.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        Category[] categories =
        [
            new() { Id = Shipping, Name = "Shipping supplies", Description = "Materials for packing orders." },
            new() { Id = Warehouse, Name = "Warehouse equipment", Description = "Equipment used to organize stock." }
        ];
        Product[] products =
        [
            new()
            {
                Id = new Guid("c01d0000-0000-4000-8000-000000000101"), Sku = "DEMO-BOX-12",
                CategoryId = Shipping, Name = "Corrugated shipping box (12 in)",
                Description = "Single-wall box for small orders.", UnitPrice = 2.25m, IsActive = true
            },
            new()
            {
                Id = new Guid("c01d0000-0000-4000-8000-000000000102"), Sku = "DEMO-TAPE-48",
                CategoryId = Shipping, Name = "Packing tape (48 mm)",
                Description = "Clear carton-sealing tape.", UnitPrice = 3.75m, IsActive = true
            },
            new()
            {
                Id = new Guid("c01d0000-0000-4000-8000-000000000103"), Sku = "DEMO-BIN-20",
                CategoryId = Warehouse, Name = "Stackable storage bin (20 L)",
                Description = "Reusable bin for picking shelves.", UnitPrice = 14.50m, IsActive = true
            }
        ];

        var categoriesAdded = 0;
        foreach (var category in categories)
        {
            if (!await db.Categories.AnyAsync(existing => existing.Id == category.Id, cancellationToken))
            {
                db.Categories.Add(category);
                categoriesAdded++;
            }
        }

        var productsAdded = 0;
        foreach (var product in products)
        {
            if (!await db.Products.AnyAsync(existing => existing.Sku == product.Sku, cancellationToken))
            {
                db.Products.Add(product);
                productsAdded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (categoriesAdded, productsAdded);
    }
}
