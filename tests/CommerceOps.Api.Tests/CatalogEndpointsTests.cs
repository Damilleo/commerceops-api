using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CommerceOps.Application.Catalog;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class CatalogEndpointsTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private const string Categories = "/api/v1/categories";
    private const string Products = "/api/v1/products";

    [Fact]
    public async Task Categories_SupportCreateListGetUpdateAndDelete()
    {
        using var client = fixture.CreateClient();
        var category = await CreateCategoryAsync(client);

        var list = await client.GetFromJsonAsync<Category[]>(Categories);
        Assert.Contains(list!, item => item.Id == category.Id);
        using var update = await client.PutAsJsonAsync($"{Categories}/{category.Id}",
            new CategoryRequest(" Updated ", "Updated description"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var updated = await client.GetFromJsonAsync<Category>($"{Categories}/{category.Id}");
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("Updated description", updated.Description);

        using var delete = await client.DeleteAsync($"{Categories}/{category.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var missing = await client.GetAsync($"{Categories}/{category.Id}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Products_SupportCreateListGetUpdateAndDelete()
    {
        using var client = fixture.CreateClient();
        var category = await CreateCategoryAsync(client);
        var otherCategory = await CreateCategoryAsync(client);
        var prefix = Guid.NewGuid().ToString("N");
        var request = new ProductRequest($" {prefix}-B ", "Product", null, 9.90m, category.Id);
        var product = await CreateProductAsync(client, request);
        var first = await CreateProductAsync(client, request with { Sku = $"{prefix}-A" });
        await CreateProductAsync(client, request with { Sku = $"{prefix}-C", CategoryId = otherCategory.Id });

        var page = await client.GetFromJsonAsync<ProductPage>($"{Products}?categoryId={category.Id}&pageSize=1");
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        var next = await client.GetFromJsonAsync<ProductPage>($"{Products}?categoryId={category.Id}&pageSize=1&page=2");
        Assert.Equal(product.Id, Assert.Single(next!.Items).Id);

        using var update = await client.PutAsJsonAsync($"{Products}/{product.Id}",
            request with { Name = "Updated product", UnitPrice = 12.34m, IsActive = false, CategoryId = otherCategory.Id });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var updated = await client.GetFromJsonAsync<Product>($"{Products}/{product.Id}");
        Assert.Equal(request.Sku!.Trim().ToUpperInvariant(), updated!.Sku);
        Assert.Equal("Updated product", updated.Name);
        Assert.Equal(12.34m, updated.UnitPrice);
        Assert.Equal(otherCategory.Id, updated.CategoryId);
        Assert.False(updated.IsActive);

        using var delete = await client.DeleteAsync($"{Products}/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var missing = await client.GetAsync($"{Products}/{product.Id}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Products_RejectDuplicateSkuAndCategoryDeletionWithDependencies()
    {
        using var client = fixture.CreateClient();
        var category = await CreateCategoryAsync(client);
        var sku = Guid.NewGuid().ToString("N");
        var request = new ProductRequest(sku, "Product", null, 4m, category.Id);
        var product = await CreateProductAsync(client, request);
        var otherProduct = await CreateProductAsync(client, request with { Sku = $"{sku}-2" });

        using var duplicate = await client.PostAsJsonAsync(Products, request with { Sku = $" {sku.ToUpperInvariant()} " });
        var duplicateProblem = await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
        Assert.Contains("SKU", duplicateProblem.GetProperty("detail").GetString());

        using var duplicateUpdate = await client.PutAsJsonAsync($"{Products}/{otherProduct.Id}", request);
        await AssertProblemAsync(duplicateUpdate, HttpStatusCode.Conflict);

        using var blockedDelete = await client.DeleteAsync($"{Categories}/{category.Id}");
        await AssertProblemAsync(blockedDelete, HttpStatusCode.Conflict);

        Assert.Equal(product.Id, (await client.GetFromJsonAsync<Product>($"{Products}/{product.Id}"))!.Id);
        Assert.Equal(category.Id, (await client.GetFromJsonAsync<Category>($"{Categories}/{category.Id}"))!.Id);
    }

    [Fact]
    public async Task Catalog_ReturnsProblemDetailsForInvalidRequests()
    {
        using var client = fixture.CreateClient();
        using var invalidCategory = await client.PostAsJsonAsync(Categories, new CategoryRequest(" ", null));
        var categoryProblem = await AssertProblemAsync(invalidCategory, HttpStatusCode.BadRequest);
        Assert.True(categoryProblem.GetProperty("errors").TryGetProperty("name", out _));

        var request = new ProductRequest("A", "Item", null, 1m, Guid.NewGuid());
        using var unknownCategory = await client.PostAsJsonAsync(Products, request);
        var productProblem = await AssertProblemAsync(unknownCategory, HttpStatusCode.BadRequest);
        Assert.True(productProblem.GetProperty("errors").TryGetProperty("categoryId", out _));

        using var invalidPage = await client.GetAsync($"{Products}?pageSize=101");
        var pageProblem = await AssertProblemAsync(invalidPage, HttpStatusCode.BadRequest);
        Assert.True(pageProblem.GetProperty("errors").TryGetProperty("pageSize", out _));

        using var invalidId = await client.GetAsync($"{Products}/invalid");
        await AssertProblemAsync(invalidId, HttpStatusCode.BadRequest);

        using var malformedJson = new StringContent("{", Encoding.UTF8, "application/json");
        using var invalidBody = await client.PostAsync(Products, malformedJson);
        await AssertProblemAsync(invalidBody, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenApi_DocumentsCatalogRoutesAndConflicts()
    {
        using var client = fixture.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");

        foreach (var collection in new[] { Categories, Products })
        {
            var list = paths.GetProperty(collection);
            Assert.True(list.TryGetProperty("get", out _));
            Assert.True(list.GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));

            var item = paths.GetProperty($"{collection}/{{id}}");
            Assert.True(item.TryGetProperty("get", out _));
            Assert.True(item.GetProperty("put").GetProperty("responses").TryGetProperty("204", out _));
            Assert.True(item.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));
        }

        Assert.True(paths.GetProperty(Products).GetProperty("post")
            .GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(paths.GetProperty($"{Categories}/{{id}}").GetProperty("delete")
            .GetProperty("responses").TryGetProperty("409", out _));
    }

    [Fact]
    public async Task Database_EnforcesUniqueSkuAndRestrictsCategoryDeletion()
    {
        using var client = fixture.CreateClient();
        var category = await CreateCategoryAsync(client);
        var request = new ProductRequest(Guid.NewGuid().ToString("N"), "Product", null, 1m, category.Id);
        var product = await CreateProductAsync(client, request);

        await using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
            store.Add(new Product { Sku = product.Sku, Name = product.Name, UnitPrice = 1m, CategoryId = category.Id });
            if (fixture.UsesSqlServer)
            {
                var conflict = await Assert.ThrowsAsync<CatalogException>(() => store.SaveChangesAsync(default));
                Assert.Equal("A product with this SKU already exists.", conflict.Message);
            }
            else
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(default));
            }
        }

        await using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
            store.Remove((await store.FindCategoryAsync(category.Id, default))!);
            if (fixture.UsesSqlServer)
            {
                var conflict = await Assert.ThrowsAsync<CatalogException>(() => store.SaveChangesAsync(default));
                Assert.Equal("The category cannot be deleted while products reference it.", conflict.Message);
            }
            else
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(default));
            }
        }
    }

    private static async Task<Category> CreateCategoryAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(Categories, new CategoryRequest("Category", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = (await response.Content.ReadFromJsonAsync<Category>())!;
        Assert.Equal($"{Categories}/{category.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(category.Id, (await client.GetFromJsonAsync<Category>(response.Headers.Location))!.Id);
        return category;
    }

    private static async Task<Product> CreateProductAsync(HttpClient client, ProductRequest request)
    {
        using var response = await client.PostAsJsonAsync(Products, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<Product>())!;
        Assert.Equal($"{Products}/{product.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(product.Id, (await client.GetFromJsonAsync<Product>(response.Headers.Location))!.Id);
        return product;
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
