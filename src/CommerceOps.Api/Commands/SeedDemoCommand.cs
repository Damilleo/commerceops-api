using CommerceOps.Infrastructure.Demo;

namespace CommerceOps.Api.Commands;

internal static class SeedDemoCommand
{
    public static async Task<int> RunAsync(IHostEnvironment environment, IServiceProvider services)
    {
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine("seed-demo is available only when the environment is Development.");
            return 1;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var seeder = scope.ServiceProvider.GetRequiredService<DemoCatalogSeeder>();
            var (categories, products) = await seeder.SeedAsync();
            Console.WriteLine($"Demo catalog ready. Added {categories} categories and {products} products.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Demo catalog seed failed. Check SQL Server readiness, connection configuration, and applied migrations.");
            return 1;
        }
    }
}
