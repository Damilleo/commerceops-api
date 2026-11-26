namespace CommerceOps.Application.Inventory;

public interface IInventoryStore
{
    Task<StockBalance?> FindBalanceAsync(Guid productId, CancellationToken cancellationToken);
    Task<StockUpdate> ApplyMovementAsync(StockMovement movement, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockMovement>> ListMovementsAsync(
        Guid productId, int offset, int limit, CancellationToken cancellationToken);
    Task<int> CountMovementsAsync(Guid productId, CancellationToken cancellationToken);
}
