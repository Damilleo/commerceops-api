using CommerceOps.Application.Catalog;

namespace CommerceOps.Api.Endpoints;

internal static class CatalogEndpoints
{
    private const string GetCategory = nameof(GetCategory);
    private const string GetProduct = nameof(GetProduct);

    public static void MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("", async (CatalogService catalog, CancellationToken cancellationToken) =>
                TypedResults.Ok(await catalog.ListCategoriesAsync(cancellationToken)))
            .WithSummary("List categories");

        categories.MapGet("/{id}", async (Guid id, CatalogService catalog, CancellationToken cancellationToken) =>
                TypedResults.Ok(await catalog.GetCategoryAsync(id, cancellationToken)))
            .WithName(GetCategory).WithSummary("Get a category by ID")
            .ProducesProblem(400).ProducesProblem(404);

        categories.MapPost("", async (CategoryRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                var category = await catalog.CreateCategoryAsync(request, cancellationToken);
                return TypedResults.CreatedAtRoute(category, GetCategory, new { id = category.Id });
            })
            .WithSummary("Create a category").ProducesValidationProblem();

        categories.MapPut("/{id}", async (Guid id, CategoryRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                await catalog.UpdateCategoryAsync(id, request, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithSummary("Replace a category").ProducesValidationProblem().ProducesProblem(404);

        categories.MapDelete("/{id}", async (Guid id, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                await catalog.DeleteCategoryAsync(id, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete a category with no associated products")
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        var products = api.MapGroup("/products").WithTags("Products");

        products.MapGet("", async (CatalogService catalog, CancellationToken cancellationToken,
                Guid? categoryId, int page = 1, int pageSize = CatalogRules.DefaultPageSize) =>
                TypedResults.Ok(await catalog.ListProductsAsync(categoryId, page, pageSize, cancellationToken)))
            .WithSummary("List products by SKU with optional category filtering and pagination")
            .ProducesValidationProblem();

        products.MapGet("/{id}", async (Guid id, CatalogService catalog, CancellationToken cancellationToken) =>
                TypedResults.Ok(await catalog.GetProductAsync(id, cancellationToken)))
            .WithName(GetProduct).WithSummary("Get a product by ID")
            .ProducesProblem(400).ProducesProblem(404);

        products.MapPost("", async (ProductRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                var product = await catalog.CreateProductAsync(request, cancellationToken);
                return TypedResults.CreatedAtRoute(product, GetProduct, new { id = product.Id });
            })
            .WithSummary("Create a product").ProducesValidationProblem().ProducesProblem(409);

        products.MapPut("/{id}", async (Guid id, ProductRequest request, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                await catalog.UpdateProductAsync(id, request, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithSummary("Replace a product")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

        products.MapDelete("/{id}", async (Guid id, CatalogService catalog, CancellationToken cancellationToken) =>
            {
                await catalog.DeleteProductAsync(id, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithSummary("Delete a product").ProducesProblem(400).ProducesProblem(404);
    }
}
