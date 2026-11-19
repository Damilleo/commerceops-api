using CommerceOps.Application.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommerceOps.Infrastructure.Catalog;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Name).IsRequired().HasMaxLength(CatalogRules.NameLength);
        builder.Property(category => category.Description).HasMaxLength(CatalogRules.DescriptionLength);
    }
}
