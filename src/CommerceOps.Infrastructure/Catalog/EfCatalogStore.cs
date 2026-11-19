using CommerceOps.Application.Common;
using CommerceOps.Application.Catalog;
using CommerceOps.Infrastructure.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Catalog;

public sealed class EfCatalogStore(CatalogDbContext db) : ICatalogStore
{
    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking().OrderBy(category => category.Name).ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);

    public async Task<Category?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Categories.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<Product>> ListProductsAsync(
        Guid? categoryId, int offset, int limit, CancellationToken cancellationToken) =>
        await FilterProducts(categoryId).OrderBy(product => product.Sku).Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountProductsAsync(Guid? categoryId, CancellationToken cancellationToken) =>
        FilterProducts(categoryId).CountAsync(cancellationToken);

    public async Task<Product?> FindProductAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.FindAsync([id], cancellationToken);

    public Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(category => category.Id == id, cancellationToken);

    public Task<bool> ProductHasMovementsAsync(Guid productId, CancellationToken cancellationToken) =>
        db.StockMovements.AnyAsync(movement => movement.ProductId == productId, cancellationToken);

    public Task<bool> CategoryHasProductsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(product => product.CategoryId == categoryId, cancellationToken);

    public Task<bool> SkuExistsAsync(string sku, Guid? exceptProductId, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(product => product.Sku == sku && product.Id != exceptProductId, cancellationToken);

    public void Add(Category category) => db.Categories.Add(category);
    public void Add(Product product) => db.Products.Add(product);
    public void Remove(Category category) => db.Categories.Remove(category);
    public void Remove(Product product) => db.Products.Remove(product);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw RequestException.NotFound(exception.Entries.Any(entry => entry.Entity is Category) ? "category" : "product");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql)
        {
            if (sql.Number is 2601 or 2627 && sql.Message.Contains(ProductConfiguration.SkuIndex, StringComparison.Ordinal))
            {
                throw CatalogErrors.DuplicateSku();
            }

            if (sql.Number == 547 && sql.Message.Contains(ProductConfiguration.CategoryForeignKey, StringComparison.Ordinal))
            {
                throw exception.Entries.Any(entry => entry.Entity is Category && entry.State == EntityState.Deleted)
                    ? CatalogErrors.CategoryInUse()
                    : CatalogErrors.UnknownCategory();
            }

            if (sql.Number == 547 && sql.Message.Contains(StockMovementConfiguration.ProductForeignKey, StringComparison.Ordinal))
            {
                throw CatalogErrors.ProductHasMovements();
            }

            throw;
        }
    }

    private IQueryable<Product> FilterProducts(Guid? categoryId)
    {
        var products = db.Products.AsNoTracking();
        return categoryId is Guid id ? products.Where(product => product.CategoryId == id) : products;
    }
}
