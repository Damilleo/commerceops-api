using CommerceOps.Application.Common;
using CommerceOps.Application.Inventory;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class InventoryRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_RejectsNonPositiveQuantity(int quantity)
    {
        var exception = Assert.Throws<RequestException>(() => InventoryRules.Validate(new StockRequest(quantity)));
        Assert.Contains("quantity", exception.Errors!);
    }

    [Fact]
    public void Validate_NormalizesOptionalTextAndRejectsExcessLength()
    {
        Assert.Equal(new StockRequest(1, "PO-1"), InventoryRules.Validate(new StockRequest(1, " PO-1 ", " ")));
        var exception = Assert.Throws<RequestException>(() => InventoryRules.Validate(
            new StockRequest(1, new string('R', 101), new string('N', 501))));
        Assert.Contains("reference", exception.Errors!);
        Assert.Contains("note", exception.Errors!);
    }

    [Theory]
    [InlineData(StockMovementType.Receipt, int.MaxValue, int.MaxValue, 0, 0)]
    [InlineData(StockMovementType.Receipt, 2, 2, 0, int.MaxValue - 2)]
    [InlineData(StockMovementType.Issue, 2, -2, 2, int.MaxValue)]
    public void GetBalanceChange_BoundsArithmetic(
        StockMovementType type, int quantity, int delta, int minimum, int maximum)
    {
        var movement = new StockMovement(Guid.NewGuid(), Guid.NewGuid(), type, quantity, DateTime.UnixEpoch, null, null);
        Assert.Equal((delta, minimum, maximum), InventoryRules.GetBalanceChange(movement));
    }
}
