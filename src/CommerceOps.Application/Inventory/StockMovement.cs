namespace CommerceOps.Application.Inventory;

public enum StockMovementType
{
    Receipt,
    Issue
}

public sealed record StockMovement(
    Guid Id, Guid ProductId, StockMovementType Type, int Quantity, DateTime OccurredAtUtc,
    string? Reference, string? Note);
