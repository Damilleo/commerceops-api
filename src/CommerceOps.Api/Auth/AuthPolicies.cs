namespace CommerceOps.Api.Auth;

internal static class AuthPolicies
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string InventoryWrite = "InventoryWrite";
    public const string LoginLimit = "LoginLimit";
    public static IReadOnlyList<string> Roles { get; } = [Admin, Operator, Viewer];
}
