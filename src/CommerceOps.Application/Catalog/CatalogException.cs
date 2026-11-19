namespace CommerceOps.Application.Catalog;

public enum CatalogError
{
    Validation,
    NotFound,
    Conflict
}

public sealed class CatalogException : Exception
{
    private CatalogException(CatalogError error, string message, Dictionary<string, string[]>? errors = null)
        : base(message)
    {
        Error = error;
        Errors = errors;
    }

    public CatalogError Error { get; }
    public Dictionary<string, string[]>? Errors { get; }

    public static CatalogException Invalid(Dictionary<string, string[]> errors) =>
        new(CatalogError.Validation, "One or more validation errors occurred.", errors);

    public static CatalogException UnknownCategory() =>
        Invalid(new Dictionary<string, string[]> { ["categoryId"] = ["The category does not exist."] });

    public static CatalogException NotFound(string resource) =>
        new(CatalogError.NotFound, $"The {resource} does not exist.");

    public static CatalogException DuplicateSku() =>
        new(CatalogError.Conflict, "A product with this SKU already exists.");

    public static CatalogException CategoryInUse() =>
        new(CatalogError.Conflict, "The category cannot be deleted while products reference it.");
}
