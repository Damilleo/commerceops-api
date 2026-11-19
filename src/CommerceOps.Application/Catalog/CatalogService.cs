namespace CommerceOps.Application.Catalog;

public sealed class CatalogService(ICatalogStore store)
{
    public Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        store.ListCategoriesAsync(cancellationToken);

    public async Task<Category> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindCategoryAsync(id, cancellationToken) ?? throw CatalogException.NotFound("category");

    public async Task<Category> CreateCategoryAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = new Category();
        ApplyCategory(category, CatalogRules.Validate(request));
        store.Add(category);
        await store.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task UpdateCategoryAsync(Guid id, CategoryRequest request, CancellationToken cancellationToken)
    {
        var validated = CatalogRules.Validate(request);
        var category = await GetCategoryAsync(id, cancellationToken);
        ApplyCategory(category, validated);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await GetCategoryAsync(id, cancellationToken);
        if (await store.CategoryHasProductsAsync(id, cancellationToken))
        {
            throw CatalogException.CategoryInUse();
        }

        store.Remove(category);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductPage> ListProductsAsync(
        Guid? categoryId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = CatalogRules.ValidatePage(page, pageSize);
        var totalCount = await store.CountProductsAsync(categoryId, cancellationToken);
        var items = await store.ListProductsAsync(categoryId, offset, pageSize, cancellationToken);
        return new ProductPage(items, page, pageSize, totalCount);
    }

    public async Task<Product> GetProductAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindProductAsync(id, cancellationToken) ?? throw CatalogException.NotFound("product");

    public async Task<Product> CreateProductAsync(ProductRequest request, CancellationToken cancellationToken)
    {
        var validated = await ValidateProductAsync(request, null, cancellationToken);
        var product = new Product();
        ApplyProduct(product, validated);
        store.Add(product);
        await store.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task UpdateProductAsync(Guid id, ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await GetProductAsync(id, cancellationToken);
        var validated = await ValidateProductAsync(request, id, cancellationToken);
        ApplyProduct(product, validated);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await GetProductAsync(id, cancellationToken);
        store.Remove(product);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProductRequest> ValidateProductAsync(
        ProductRequest request, Guid? exceptProductId, CancellationToken cancellationToken)
    {
        var validated = CatalogRules.Validate(request);
        if (!await store.CategoryExistsAsync(validated.CategoryId, cancellationToken))
        {
            throw CatalogException.UnknownCategory();
        }

        if (await store.SkuExistsAsync(validated.Sku!, exceptProductId, cancellationToken))
        {
            throw CatalogException.DuplicateSku();
        }

        return validated;
    }

    private static void ApplyCategory(Category category, CategoryRequest request)
    {
        category.Name = request.Name!;
        category.Description = request.Description;
    }

    private static void ApplyProduct(Product product, ProductRequest request)
    {
        product.Sku = request.Sku!;
        product.Name = request.Name!;
        product.Description = request.Description;
        product.UnitPrice = request.UnitPrice;
        product.CategoryId = request.CategoryId;
        product.IsActive = request.IsActive;
    }
}
