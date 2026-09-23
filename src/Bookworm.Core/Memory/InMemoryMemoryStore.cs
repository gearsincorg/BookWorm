namespace Bookworm.Core.Memory;

/// <summary>Session-only fallback for use before Azure memory-store credentials are configured, and in
/// tests — does not actually persist anything across process runs.</summary>
public sealed class InMemoryMemoryStore : IMemoryStore
{
    private BookwormMemory _memory = new();

    public Task<MemoryLoadResult> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(new MemoryLoadResult(_memory, null));

    public Task<string?> SaveAsync(BookwormMemory memory, string? etag, CancellationToken ct = default)
    {
        _memory = memory;
        return Task.FromResult<string?>(null);
    }
}
