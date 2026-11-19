namespace CommerceOps.Application.Catalog;

public static class CatalogRules
{
    public const int NameLength = 120;
    public const int DescriptionLength = 1000;
    public const int SkuLength = 64;
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static CategoryRequest Validate(CategoryRequest request)
    {
        var name = request.Name?.Trim();
        var description = NormalizeDescription(request.Description);
        var errors = new Dictionary<string, string[]>();

        ValidateText(errors, "name", name, NameLength);
        ValidateDescription(errors, description);
        ThrowIfInvalid(errors);
        return new CategoryRequest(name, description);
    }

    public static ProductRequest Validate(ProductRequest request)
    {
        var sku = request.Sku?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var description = NormalizeDescription(request.Description);
        var errors = new Dictionary<string, string[]>();

        ValidateText(errors, "sku", sku, SkuLength);
        ValidateText(errors, "name", name, NameLength);
        ValidateDescription(errors, description);

        if (request.UnitPrice <= 0 || request.UnitPrice > 9_999_999_999_999_999.99m ||
            decimal.Round(request.UnitPrice, PriceScale) != request.UnitPrice)
        {
            errors["unitPrice"] = ["Unit price must be positive and fit decimal(18,2)."];
        }

        if (request.CategoryId == Guid.Empty)
        {
            errors["categoryId"] = ["A category ID is required."];
        }

        ThrowIfInvalid(errors);
        return request with { Sku = sku, Name = name, Description = description };
    }

    public static int ValidatePage(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            errors["page"] = ["Page exceeds the supported range for this page size."];
        }

        ThrowIfInvalid(errors);
        return (int)offset;
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateText(Dictionary<string, string[]> errors, string field, string? value, int limit)
    {
        if (string.IsNullOrEmpty(value) || value.Length > limit)
        {
            errors[field] = [$"Must contain between 1 and {limit} characters."];
        }
    }

    private static void ValidateDescription(Dictionary<string, string[]> errors, string? description)
    {
        if (description?.Length > DescriptionLength)
        {
            errors["description"] = [$"Must contain no more than {DescriptionLength} characters."];
        }
    }

    private static void ThrowIfInvalid(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw CatalogException.Invalid(errors);
        }
    }
}
