using System.Data;
using CommerceOps.Application.Common;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Api.Auth;

internal sealed class AdminBootstrap(
    CatalogDbContext db, UserManager<IdentityUser<Guid>> users, RoleManager<IdentityRole<Guid>> roles)
{
    public async Task<bool> RunAsync(string? userName, string? password)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // The Admin role is created only here and commits together with the first admin.
        if (await roles.RoleExistsAsync(AuthPolicies.Admin))
        {
            return false;
        }

        foreach (var role in AuthPolicies.Roles)
        {
            if (!(await roles.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = role })).Succeeded)
            {
                throw new InvalidOperationException("Identity could not initialize its roles.");
            }
        }

        await AccountService.CreateIdentityUserAsync(users, userName, password, AuthPolicies.Admin);
        await transaction.CommitAsync();
        return true;
    }

    public static async Task<int> RunCommandAsync(IServiceProvider services)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
            var created = await bootstrap.RunAsync(Environment.GetEnvironmentVariable("COMMERCEOPS_ADMIN_USERNAME"),
                Environment.GetEnvironmentVariable("COMMERCEOPS_ADMIN_PASSWORD"));
            Console.WriteLine(created ? "Initial administrator created." : "Administrator bootstrap already completed.");
            return 0;
        }
        catch (RequestException error) when (error.Errors is not null)
        {
            foreach (var message in error.Errors.Values.SelectMany(messages => messages))
            {
                Console.Error.WriteLine(message);
            }

            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Administrator bootstrap failed. Check the database configuration and migrations.");
            return 1;
        }
    }
}
