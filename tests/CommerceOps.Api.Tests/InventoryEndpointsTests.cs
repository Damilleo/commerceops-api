using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Application.Catalog;
using CommerceOps.Application.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class InventoryEndpointsTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private static readonly JsonSerializerOptions InventoryJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<StockMovementType>(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task ReceiptAndIssue_UpdateBalanceAndRecordPaginatedHistory()
    {
        using var client = fixture.CreateAdminClient();
        var route = await CreateInventoryAsync(client);

        Assert.Equal(0, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);
        var receipt = await ApplyAsync(client, $"{route}/receipts", new StockRequest(5, " PO-5 "));
        Assert.Equal(5, receipt.Balance.StockQuantity);
        Assert.Equal("PO-5", receipt.Movement.Reference);
        Assert.Equal(DateTimeKind.Utc, receipt.Movement.OccurredAtUtc.Kind);
        Assert.Equal(StockMovementType.Receipt, receipt.Movement.Type);
        Assert.Equal(5, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);

        var issue = await ApplyAsync(client, $"{route}/issues", new StockRequest(2, Note: "Shipment"));
        Assert.Equal(3, issue.Balance.StockQuantity);
        Assert.Equal(StockMovementType.Issue, issue.Movement.Type);
        Assert.Equal("Shipment", issue.Movement.Note);
        Assert.Equal(3, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);

        var first = await GetHistoryAsync(client, $"{route}/movements?pageSize=1");
        var second = await GetHistoryAsync(client, $"{route}/movements?page=2&pageSize=1");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(issue.Movement.Id, Assert.Single(first.Items).Id);
        Assert.Equal(receipt.Movement.Id, Assert.Single(second.Items).Id);
    }

    [Fact]
    public async Task IssuesAndReceipts_RejectInsufficientStockAndOverflowWithoutSideEffects()
    {
        using var client = fixture.CreateAdminClient();
        var route = await CreateInventoryAsync(client);

        using var insufficient = await client.PostAsJsonAsync($"{route}/issues", new StockRequest(1));
        var problem = await AssertProblemAsync(insufficient, HttpStatusCode.Conflict);
        Assert.Contains("Insufficient stock", problem.GetProperty("detail").GetString());
        Assert.Empty((await GetHistoryAsync(client, $"{route}/movements")).Items);

        await ApplyAsync(client, $"{route}/receipts", new StockRequest(int.MaxValue));
        using var overflow = await client.PostAsJsonAsync($"{route}/receipts", new StockRequest(1));
        await AssertProblemAsync(overflow, HttpStatusCode.Conflict);
        Assert.Equal(int.MaxValue, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);
        Assert.Single((await GetHistoryAsync(client, $"{route}/movements")).Items);
    }

    [Fact]
    public async Task Inventory_ReturnsProblemDetailsForMissingProductsAndInvalidInput()
    {
        using var client = fixture.CreateAdminClient();
        var missingRoute = $"/api/v1/inventory/{Guid.NewGuid()}";
        using var missingBalance = await client.GetAsync(missingRoute);
        await AssertProblemAsync(missingBalance, HttpStatusCode.NotFound);
        using var missingHistory = await client.GetAsync($"{missingRoute}/movements");
        await AssertProblemAsync(missingHistory, HttpStatusCode.NotFound);
        using var missingReceipt = await client.PostAsJsonAsync($"{missingRoute}/receipts", new StockRequest(1));
        await AssertProblemAsync(missingReceipt, HttpStatusCode.NotFound);
        using var missingIssue = await client.PostAsJsonAsync($"{missingRoute}/issues", new StockRequest(1));
        await AssertProblemAsync(missingIssue, HttpStatusCode.NotFound);

        var route = await CreateInventoryAsync(client);
        using var invalidReceipt = await client.PostAsJsonAsync($"{route}/receipts", new StockRequest(0));
        var invalidQuantity = await AssertProblemAsync(invalidReceipt, HttpStatusCode.BadRequest);
        Assert.True(invalidQuantity.GetProperty("errors").TryGetProperty("quantity", out _));
        using var fractionalIssue = await client.PostAsJsonAsync($"{route}/issues", new { Quantity = 0.5m });
        await AssertProblemAsync(fractionalIssue, HttpStatusCode.BadRequest);
        using var invalidPage = await client.GetAsync($"{route}/movements?page=0");
        await AssertProblemAsync(invalidPage, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Products_CannotBeDeletedAfterMovementEvenWhenBalanceIsZero()
    {
        using var client = fixture.CreateAdminClient();
        var route = await CreateInventoryAsync(client);
        var receipt = await ApplyAsync(client, $"{route}/receipts", new StockRequest(1));
        await ApplyAsync(client, $"{route}/issues", new StockRequest(1));

        using var delete = await client.DeleteAsync($"/api/v1/products/{receipt.Balance.ProductId}");
        await AssertProblemAsync(delete, HttpStatusCode.Conflict);
        Assert.Equal(0, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);
        Assert.Equal(2, (await GetHistoryAsync(client, $"{route}/movements")).TotalCount);
    }

    [Fact]
    public async Task FailedMovementInsert_RollsBackBalanceChange()
    {
        using var client = fixture.CreateAdminClient();
        var route = await CreateInventoryAsync(client);
        var receipt = await ApplyAsync(client, $"{route}/receipts", new StockRequest(3));

        await using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInventoryStore>();
            // Reusing the movement ID forces the insert to fail after the balance update.
            await Assert.ThrowsAsync<DbUpdateException>(() => store.ApplyMovementAsync(receipt.Movement, default));
        }

        Assert.Equal(3, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);
        Assert.Single((await GetHistoryAsync(client, $"{route}/movements")).Items);
    }

    [SqlServerFact]
    public async Task ConcurrentIssues_OnlyOneCanSpendTheLastUnit()
    {
        using var client = fixture.CreateAdminClient();
        var route = await CreateInventoryAsync(client);
        await ApplyAsync(client, $"{route}/receipts", new StockRequest(1));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> SendIssueAsync()
        {
            await start.Task;
            return await client.PostAsJsonAsync($"{route}/issues", new StockRequest(1));
        }

        var first = SendIssueAsync();
        var second = SendIssueAsync();
        start.SetResult();
        await Task.WhenAll(first, second);
        using var firstResponse = await first;
        using var secondResponse = await second;
        var responses = new[] { firstResponse, secondResponse };
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict);

        Assert.Equal(0, (await client.GetFromJsonAsync<StockBalance>(route))!.StockQuantity);
        var history = await GetHistoryAsync(client, $"{route}/movements");
        Assert.Equal(2, history.TotalCount);
        Assert.Single(history.Items, movement => movement.Type == StockMovementType.Issue);
    }

    [Fact]
    public async Task OpenApi_DocumentsInventoryOperations()
    {
        using var client = fixture.CreateAdminClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        const string route = "/api/v1/inventory/{productId}";
        Assert.True(paths.GetProperty(route).TryGetProperty("get", out _));
        Assert.True(paths.GetProperty($"{route}/movements").TryGetProperty("get", out _));
        foreach (var operation in new[] { "receipts", "issues" })
        {
            var responses = paths.GetProperty($"{route}/{operation}").GetProperty("post").GetProperty("responses");
            Assert.True(responses.TryGetProperty("409", out _));
        }
    }

    private static async Task<string> CreateInventoryAsync(HttpClient client)
    {
        using var categoryResponse = await client.PostAsJsonAsync("/api/v1/categories", new CategoryRequest("Inventory", null));
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<Category>())!;
        using var productResponse = await client.PostAsJsonAsync("/api/v1/products",
            new ProductRequest(Guid.NewGuid().ToString("N"), "Inventory product", null, 1m, category.Id));
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = (await productResponse.Content.ReadFromJsonAsync<Product>())!;
        return $"/api/v1/inventory/{product.Id}";
    }

    private static async Task<StockUpdate> ApplyAsync(HttpClient client, string route, StockRequest request)
    {
        using var response = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StockUpdate>(InventoryJson))!;
    }

    private static async Task<MovementPage> GetHistoryAsync(HttpClient client, string route) =>
        (await client.GetFromJsonAsync<MovementPage>(route, InventoryJson))!;

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
