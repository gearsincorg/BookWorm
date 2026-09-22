namespace Bookworm.Core.Auth;

/// <summary>
/// Secure storage for secrets (VA login, Azure connection string, Anthropic API key).
/// Implemented per-platform (e.g. Windows Credential Manager) — never store secrets in plaintext config.
/// </summary>
public interface ICredentialStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task SetSecretAsync(string key, string secret, CancellationToken ct = default);
    Task DeleteSecretAsync(string key, CancellationToken ct = default);
}

public static class CredentialKeys
{
    public const string VisionAustralia = "Bookworm:VisionAustralia";
    public const string AzureMemoryStore = "Bookworm:AzureMemoryStore";
    public const string AnthropicApiKey = "Bookworm:AnthropicApiKey";
}
