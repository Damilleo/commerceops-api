using CommerceOps.Application.Common;
using CommerceOps.Application.Catalog;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class CatalogRulesTests
{
    [Fact]
    public void ValidateProduct_NormalizesSkuAndText()
    {
        var request = new ProductRequest(" sku-01 ", " Item ", " ", 1.25m, Guid.NewGuid());

        var validated = CatalogRules.Validate(request);

        Assert.Equal("SKU-01", validated.Sku);
        Assert.Equal("Item", validated.Name);
        Assert.Null(validated.Description);
        Assert.True(validated.IsActive);
    }

    [Fact]
    public void ValidateProduct_ReportsEachInvalidField()
    {
        var request = new ProductRequest(new string('A', 65), " ", new string('D', 1001), 0m, Guid.Empty);

        var exception = Assert.Throws<RequestException>(() => CatalogRules.Validate(request));

        Assert.Equal(RequestError.Validation, exception.Error);
        Assert.Equal(["sku", "name", "description", "unitPrice", "categoryId"], exception.Errors!.Keys);
    }

    [Fact]
    public void ValidateProduct_RejectsPricesOutsideDecimalColumn()
    {
        var request = new ProductRequest("SKU", "Item", null, 1m, Guid.NewGuid());

        Assert.Throws<RequestException>(() => CatalogRules.Validate(request with { UnitPrice = 0.001m }));
        Assert.Throws<RequestException>(() => CatalogRules.Validate(request with { UnitPrice = 10_000_000_000_000_000m }));
        Assert.Equal(9_999_999_999_999_999.99m,
            CatalogRules.Validate(request with { UnitPrice = 9_999_999_999_999_999.99m }).UnitPrice);
    }

    [Fact]
    public void ValidateCategory_RejectsBlankNameAndOverlongDescription()
    {
        var request = new CategoryRequest(" ", new string('D', 1001));

        var exception = Assert.Throws<RequestException>(() => CatalogRules.Validate(request));

        Assert.Equal(["name", "description"], exception.Errors!.Keys);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    [InlineData(int.MaxValue, 100, "page")]
    public void ValidatePage_RejectsInvalidPageOrSize(int page, int pageSize, string field)
    {
        var exception = Assert.Throws<RequestException>(() => Pagination.GetOffset(page, pageSize));

        Assert.Contains(field, exception.Errors!);
    }
}
