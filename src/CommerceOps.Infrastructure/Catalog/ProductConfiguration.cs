using CommerceOps.Application.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommerceOps.Infrastructure.Catalog;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    internal const string StockQuantity = "StockQuantity";
    internal const string SkuIndex = "UX_Products_Sku";
    internal const string CategoryForeignKey = "FK_Products_Categories_CategoryId";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Products_UnitPrice_Positive", "CAST(UnitPrice AS decimal(18,2)) > 0");
            table.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "StockQuantity >= 0");
        });
        builder.Property<int>(StockQuantity).HasDefaultValue(0);
        builder.Property(product => product.Id).ValueGeneratedNever();
        builder.Property(product => product.Sku).IsRequired().HasMaxLength(CatalogRules.SkuLength);
        builder.Property(product => product.Name).IsRequired().HasMaxLength(CatalogRules.NameLength);
        builder.Property(product => product.Description).HasMaxLength(CatalogRules.DescriptionLength);
        builder.Property(product => product.UnitPrice).HasPrecision(CatalogRules.PricePrecision, CatalogRules.PriceScale);
        builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName(SkuIndex);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(product => product.CategoryId)
            .HasConstraintName(CategoryForeignKey)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
