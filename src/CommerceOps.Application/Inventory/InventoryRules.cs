using CommerceOps.Application.Common;

namespace CommerceOps.Application.Inventory;

public static class InventoryRules
{
    public const int ReferenceLength = 100;
    public const int NoteLength = 500;

    public static StockRequest Validate(StockRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Quantity <= 0)
        {
            errors["quantity"] = ["Quantity must be a positive integer."];
        }

        var reference = NormalizeText(request.Reference);
        var note = NormalizeText(request.Note);

        if (reference?.Length > ReferenceLength)
        {
            errors["reference"] = [$"Must contain no more than {ReferenceLength} characters."];
        }
        if (note?.Length > NoteLength)
        {
            errors["note"] = [$"Must contain no more than {NoteLength} characters."];
        }
        if (errors.Count > 0)
        {
            throw RequestException.Invalid(errors);
        }

        return request with { Reference = reference, Note = note };
    }

    public static (int Delta, int MinimumBalance, int MaximumBalance) GetBalanceChange(StockMovement movement) =>
        movement.Type switch
        {
            StockMovementType.Receipt => (movement.Quantity, 0, int.MaxValue - movement.Quantity),
            StockMovementType.Issue => (-movement.Quantity, movement.Quantity, int.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(movement))
        };

    private static string? NormalizeText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
