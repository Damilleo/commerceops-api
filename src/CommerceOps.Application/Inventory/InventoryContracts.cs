namespace CommerceOps.Application.Inventory;

public sealed record StockRequest(int Quantity, string? Reference = null, string? Note = null);

public sealed record StockBalance(Guid ProductId, int StockQuantity);

public sealed record StockUpdate(StockBalance Balance, StockMovement Movement);

public sealed record MovementPage(IReadOnlyList<StockMovement> Items, int Page, int PageSize, int TotalCount);
