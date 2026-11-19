namespace CommerceOps.Application.Catalog;

public sealed class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid CategoryId { get; set; }
    public bool IsActive { get; set; }
}
