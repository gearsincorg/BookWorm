using System.Text.Json;

namespace Bookworm.Core.Auth;

/// <summary>Azure AI Speech resource key + region, as stored via <see cref="ICredentialStore"/>.</summary>
public sealed class AzureSpeechCredentials
{
    public required string Key { get; init; }
    public required string Region { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static AzureSpeechCredentials FromJson(string json) =>
        JsonSerializer.Deserialize<AzureSpeechCredentials>(json)
        ?? throw new InvalidOperationException("Stored Azure Speech credentials could not be parsed.");

    public static async Task<AzureSpeechCredentials?> LoadAsync(ICredentialStore store, CancellationToken ct = default)
    {
        var json = await store.GetSecretAsync(CredentialKeys.AzureSpeech, ct);
        return json is null ? null : FromJson(json);
    }

    public async Task SaveAsync(ICredentialStore store, CancellationToken ct = default) =>
        await store.SetSecretAsync(CredentialKeys.AzureSpeech, ToJson(), ct);
}
