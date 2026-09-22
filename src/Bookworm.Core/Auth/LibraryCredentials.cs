using System.Text.Json;

namespace Bookworm.Core.Auth;

/// <summary>VA login (email or VAID + password), as stored (serialized) via <see cref="ICredentialStore"/>.</summary>
public sealed class LibraryCredentials
{
    public required string EmailOrVaid { get; init; }
    public required string Password { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LibraryCredentials FromJson(string json) =>
        JsonSerializer.Deserialize<LibraryCredentials>(json)
        ?? throw new InvalidOperationException("Stored VA credentials could not be parsed.");

    public static async Task<LibraryCredentials?> LoadAsync(ICredentialStore store, CancellationToken ct = default)
    {
        var json = await store.GetSecretAsync(CredentialKeys.VisionAustralia, ct);
        return json is null ? null : FromJson(json);
    }

    public async Task SaveAsync(ICredentialStore store, CancellationToken ct = default) =>
        await store.SetSecretAsync(CredentialKeys.VisionAustralia, ToJson(), ct);
}
