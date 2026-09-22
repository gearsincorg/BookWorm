using System.Text.Json;

namespace Bookworm.Core.Auth;

/// <summary>Azure Storage connection string + container name for <see cref="Memory.AzureBlobMemoryStore"/>, as stored via <see cref="ICredentialStore"/>.</summary>
public sealed class AzureMemoryStoreCredentials
{
    public required string ConnectionString { get; init; }
    public required string ContainerName { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static AzureMemoryStoreCredentials FromJson(string json) =>
        JsonSerializer.Deserialize<AzureMemoryStoreCredentials>(json)
        ?? throw new InvalidOperationException("Stored Azure memory-store credentials could not be parsed.");

    public static async Task<AzureMemoryStoreCredentials?> LoadAsync(ICredentialStore store, CancellationToken ct = default)
    {
        var json = await store.GetSecretAsync(CredentialKeys.AzureMemoryStore, ct);
        return json is null ? null : FromJson(json);
    }

    public async Task SaveAsync(ICredentialStore store, CancellationToken ct = default) =>
        await store.SetSecretAsync(CredentialKeys.AzureMemoryStore, ToJson(), ct);
}
