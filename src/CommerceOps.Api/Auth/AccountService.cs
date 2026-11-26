using System.Security.Cryptography;
using CommerceOps.Application.Common;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Api.Auth;

internal sealed record LoginRequest(string? UserName, string? Password);
internal sealed record CreateUserRequest(string? UserName, string? Password, string? Role);
internal sealed record AccountResponse(Guid Id, string UserName, IReadOnlyList<string> Roles);

internal sealed class AccountService(
    CatalogDbContext db, UserManager<IdentityUser<Guid>> users,
    SignInManager<IdentityUser<Guid>> signIn, AccessTokenIssuer tokens)
{
    private const int UserNameLimit = 256;
    private const int PasswordLimit = 128;
    private static readonly IdentityUser<Guid> DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<IdentityUser<Guid>>()
        .HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public async Task<TokenResponse?> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > UserNameLimit ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > PasswordLimit)
        {
            return null;
        }

        var user = await users.FindByNameAsync(request.UserName.Trim());
        if (user is not null)
        {
            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return tokens.Issue(user, await users.GetRolesAsync(user));
            }

            if (!result.IsLockedOut)
            {
                return null;
            }
        }

        // Spend comparable hashing work for unknown and locked-out accounts.
        users.PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
        return null;
    }

    public async Task<AccountResponse> CreateAsync(CreateUserRequest request)
    {
        if (!AuthPolicies.Roles.Contains(request.Role, StringComparer.Ordinal))
        {
            throw RequestException.Invalid(new() { ["role"] = ["Choose Admin, Operator, or Viewer."] });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await CreateIdentityUserAsync(users, request.UserName, request.Password, request.Role!);
        await transaction.CommitAsync();
        return new AccountResponse(user.Id, user.UserName!, [request.Role!]);
    }

    internal static async Task<IdentityUser<Guid>> CreateIdentityUserAsync(
        UserManager<IdentityUser<Guid>> users, string? userName, string? password, string role)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > UserNameLimit)
        {
            throw RequestException.Invalid(new() { ["userName"] = ["Enter a username of 1 to 256 characters."] });
        }

        if (string.IsNullOrEmpty(password) || password.Length > PasswordLimit)
        {
            throw RequestException.Invalid(new() { ["password"] = ["Enter a password of at most 128 characters."] });
        }

        var user = new IdentityUser<Guid> { Id = Guid.NewGuid(), UserName = name };
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql &&
            sql.Number is 2601 or 2627 && sql.Message.Contains("UserNameIndex", StringComparison.Ordinal))
        {
            throw RequestException.Conflict("This username is already in use.");
        }

        if (!created.Succeeded)
        {
            var codes = created.Errors.Select(error => error.Code).ToArray();
            if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                throw RequestException.Conflict("This username is already in use.");
            }

            var errors = new Dictionary<string, string[]>();
            if (codes.Contains(nameof(IdentityErrorDescriber.InvalidUserName)))
            {
                errors["userName"] = ["Use only letters, digits, or the characters -._@+."];
            }
            if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
            {
                errors["password"] = ["Use at least 15 characters, including uppercase, lowercase, a digit, and a symbol."];
            }
            if (errors.Count > 0)
            {
                throw RequestException.Invalid(errors);
            }

            throw new InvalidOperationException("Identity could not create the user.");
        }

        if (!(await users.AddToRoleAsync(user, role)).Succeeded)
        {
            throw new InvalidOperationException("Identity could not assign the requested role.");
        }

        return user;
    }
}
