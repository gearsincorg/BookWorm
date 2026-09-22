using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Bookworm.Core.Memory;

/// <summary>
/// A single versioned JSON blob holding <see cref="BookwormMemory"/> — chosen over a database because
/// this is one small document with infrequent writes from effectively one active client at a time (see
/// docs/decisions.md's Cross-session memory section). Portable (not Windows-only), so this is directly
/// reusable by the future MAUI/Android app.
/// </summary>
public sealed class AzureBlobMemoryStore : IMemoryStore
{
    private readonly BlobClient _blob;

    public AzureBlobMemoryStore(string connectionString, string containerName, string blobName = "memory.json")
    {
        var container = new BlobContainerClient(connectionString, containerName);
        container.CreateIfNotExists();
        _blob = container.GetBlobClient(blobName);
    }

    public async Task<MemoryLoadResult> LoadAsync(CancellationToken ct = default)
    {
        if (!await _blob.ExistsAsync(ct))
        {
            return new MemoryLoadResult(new BookwormMemory(), null);
        }

        var response = await _blob.DownloadContentAsync(ct);
        var memory = JsonSerializer.Deserialize<BookwormMemory>(response.Value.Content.ToString()) ?? new BookwormMemory();
        return new MemoryLoadResult(memory, response.Value.Details.ETag.ToString());
    }

    public async Task SaveAsync(BookwormMemory memory, string? etag, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(memory);
        var options = new BlobUploadOptions();
        if (etag is not null)
        {
            options.Conditions = new BlobRequestConditions { IfMatch = new ETag(etag) };
        }
        await _blob.UploadAsync(BinaryData.FromString(json), options, ct);
    }
}
