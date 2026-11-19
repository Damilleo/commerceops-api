using CommerceOps.Application.Catalog;
using CommerceOps.Application.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommerceOps.Infrastructure.Inventory;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    internal const string ProductForeignKey = "FK_StockMovements_Products_ProductId";

    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Quantity_Positive", "Quantity > 0");
            table.HasCheckConstraint("CK_StockMovements_Type", "Type IN ('Receipt', 'Issue')");
        });
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(7);
        builder.Property(movement => movement.OccurredAtUtc)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(movement => movement.Reference).HasMaxLength(InventoryRules.ReferenceLength);
        builder.Property(movement => movement.Note).HasMaxLength(InventoryRules.NoteLength);
        builder.HasIndex(movement => new { movement.ProductId, movement.OccurredAtUtc, movement.Id })
            .IsDescending(false, true, true);
        builder.HasOne<Product>().WithMany().HasForeignKey(movement => movement.ProductId)
            .HasConstraintName(ProductForeignKey).OnDelete(DeleteBehavior.Restrict);
    }
}
