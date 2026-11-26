using CommerceOps.Application.Common;
using CommerceOps.Application.Inventory;

namespace CommerceOps.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        var inventory = api.MapGroup("/inventory/{productId}").WithTags("Inventory");

        inventory.MapGet("", async (Guid productId, InventoryService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.GetBalanceAsync(productId, cancellationToken)))
            .WithSummary("Get a product's current stock quantity")
            .ProducesProblem(400).ProducesProblem(404);

        inventory.MapPost("/receipts", async (Guid productId, StockRequest request,
                InventoryService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ReceiveAsync(productId, request, cancellationToken)))
            .WithSummary("Receive stock and return the new balance and movement")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

        inventory.MapPost("/issues", async (Guid productId, StockRequest request,
                InventoryService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.IssueAsync(productId, request, cancellationToken)))
            .WithSummary("Issue available stock and return the new balance and movement")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

        inventory.MapGet("/movements", async (Guid productId, InventoryService service, CancellationToken cancellationToken,
                int page = 1, int pageSize = Pagination.DefaultPageSize) =>
                TypedResults.Ok(await service.GetMovementsAsync(productId, page, pageSize, cancellationToken)))
            .WithSummary("List stock movements newest first")
            .ProducesValidationProblem().ProducesProblem(404);
    }
}
