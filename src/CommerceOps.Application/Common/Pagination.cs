namespace CommerceOps.Application.Common;

public static class Pagination
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static int GetOffset(int page, int pageSize)
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

        if (errors.Count > 0)
        {
            throw RequestException.Invalid(errors);
        }

        return (int)offset;
    }
}
