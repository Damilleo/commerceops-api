using CommerceOps.Application.Common;

namespace CommerceOps.Application.Catalog;

public static class CatalogErrors
{
    public static RequestException UnknownCategory() =>
        RequestException.Invalid(new Dictionary<string, string[]> { ["categoryId"] = ["The category does not exist."] });

    public static RequestException DuplicateSku() =>
        RequestException.Conflict("A product with this SKU already exists.");

    public static RequestException CategoryInUse() =>
        RequestException.Conflict("The category cannot be deleted while products reference it.");

    public static RequestException ProductHasMovements() =>
        RequestException.Conflict("The product cannot be deleted while it has stock movements.");
}
