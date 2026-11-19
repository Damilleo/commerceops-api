namespace CommerceOps.Application.Catalog;

public interface ICatalogStore
{
    Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken cancellationToken);
    Task<Category?> FindCategoryAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ListProductsAsync(Guid? categoryId, int offset, int limit, CancellationToken cancellationToken);
    Task<int> CountProductsAsync(Guid? categoryId, CancellationToken cancellationToken);
    Task<Product?> FindProductAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ProductHasMovementsAsync(Guid productId, CancellationToken cancellationToken);
    Task<bool> CategoryHasProductsAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<bool> SkuExistsAsync(string sku, Guid? exceptProductId, CancellationToken cancellationToken);

    void Add(Category category);
    void Add(Product product);
    void Remove(Category category);
    void Remove(Product product);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
