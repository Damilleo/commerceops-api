using CommerceOps.Application.Common;
using CommerceOps.Application.Inventory;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Inventory;

public sealed class EfInventoryStore(CatalogDbContext db) : IInventoryStore
{
    public Task<StockBalance?> FindBalanceAsync(Guid productId, CancellationToken cancellationToken) =>
        db.Products.Where(product => product.Id == productId)
            .Select(product => new StockBalance(product.Id, EF.Property<int>(product, ProductConfiguration.StockQuantity)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<StockUpdate> ApplyMovementAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var (delta, minimum, maximum) = InventoryRules.GetBalanceChange(movement);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // SQL evaluates the bounds and updates the current balance in the same statement.
            var affected = await db.Products.Where(product => product.Id == movement.ProductId &&
                    EF.Property<int>(product, ProductConfiguration.StockQuantity) >= minimum &&
                    EF.Property<int>(product, ProductConfiguration.StockQuantity) <= maximum)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    product => EF.Property<int>(product, ProductConfiguration.StockQuantity),
                    product => EF.Property<int>(product, ProductConfiguration.StockQuantity) + delta), cancellationToken);

            var balance = await FindBalanceAsync(movement.ProductId, cancellationToken)
                ?? throw RequestException.NotFound("product");

            if (affected == 0)
            {
                throw RequestException.Conflict(movement.Type == StockMovementType.Issue
                    ? "Insufficient stock for this issue."
                    : "This receipt would exceed the maximum stock quantity.");
            }

            db.StockMovements.Add(movement);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StockUpdate(balance, movement);
        }
        catch (SqlException exception) when (IsConcurrencyConflict(exception))
        {
            throw RequestException.Conflict("The stock update conflicted with another operation. Try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && IsConcurrencyConflict(sql))
        {
            throw RequestException.Conflict("The stock update conflicted with another operation. Try again.");
        }
    }

    public async Task<IReadOnlyList<StockMovement>> ListMovementsAsync(
        Guid productId, int offset, int limit, CancellationToken cancellationToken) =>
        await db.StockMovements.AsNoTracking().Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.OccurredAtUtc).ThenByDescending(movement => movement.Id)
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);

    public Task<int> CountMovementsAsync(Guid productId, CancellationToken cancellationToken) =>
        db.StockMovements.CountAsync(movement => movement.ProductId == productId, cancellationToken);

    private static bool IsConcurrencyConflict(SqlException exception) => exception.Number is 1205 or 1222;
}
