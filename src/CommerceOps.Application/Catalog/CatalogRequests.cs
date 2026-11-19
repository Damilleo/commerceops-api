namespace CommerceOps.Application.Catalog;

public sealed record CategoryRequest(string? Name, string? Description);

public sealed record ProductRequest(
    string? Sku, string? Name, string? Description, decimal UnitPrice, Guid CategoryId, bool IsActive = true);

public sealed record ProductPage(IReadOnlyList<Product> Items, int Page, int PageSize, int TotalCount);
