namespace CommerceOps.Application.Health;

public sealed record ServiceHealth(string Status)
{
    public static ServiceHealth GetLiveness() => new("healthy");
}
