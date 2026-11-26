namespace CommerceOps.Api.Auth;

internal sealed class JwtSettings
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";

    public bool IsValid()
    {
        Span<byte> buffer = new byte[SigningKey.Length];
        return !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience) &&
            Convert.TryFromBase64String(SigningKey, buffer, out var written) && written >= 32;
    }
}
