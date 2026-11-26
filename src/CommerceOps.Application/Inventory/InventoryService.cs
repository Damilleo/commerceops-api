using CommerceOps.Application.Common;

namespace CommerceOps.Application.Inventory;

public sealed class InventoryService(IInventoryStore store, TimeProvider clock)
{
    public async Task<StockBalance> GetBalanceAsync(Guid productId, CancellationToken cancellationToken) =>
        await store.FindBalanceAsync(productId, cancellationToken) ?? throw RequestException.NotFound("product");

    public Task<StockUpdate> ReceiveAsync(Guid productId, StockRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(productId, StockMovementType.Receipt, request, cancellationToken);

    public Task<StockUpdate> IssueAsync(Guid productId, StockRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(productId, StockMovementType.Issue, request, cancellationToken);

    public async Task<MovementPage> GetMovementsAsync(
        Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = Pagination.GetOffset(page, pageSize);
        await GetBalanceAsync(productId, cancellationToken);
        var total = await store.CountMovementsAsync(productId, cancellationToken);
        var movements = await store.ListMovementsAsync(productId, offset, pageSize, cancellationToken);
        return new MovementPage(movements, page, pageSize, total);
    }

    private Task<StockUpdate> ApplyAsync(
        Guid productId, StockMovementType type, StockRequest request, CancellationToken cancellationToken)
    {
        var validated = InventoryRules.Validate(request);
        var movement = new StockMovement(Guid.NewGuid(), productId, type, validated.Quantity,
            clock.GetUtcNow().UtcDateTime, validated.Reference, validated.Note);
        return store.ApplyMovementAsync(movement, cancellationToken);
    }
}
