namespace Bookworm.Core.Memory;

public sealed record MemoryLoadResult(BookwormMemory Memory, string? ETag);

public interface IMemoryStore
{
    Task<MemoryLoadResult> LoadAsync(CancellationToken ct = default);

    /// <param name="etag">Pass the ETag from <see cref="LoadAsync"/> (or a previous <see cref="SaveAsync"/>)
    /// for optimistic concurrency; null to write unconditionally (first save, or when overwriting is
    /// intentional).</param>
    /// <returns>The new ETag — callers MUST use this for their next save, not the one they passed in.
    /// Every successful write changes the blob's ETag, so reusing a stale one fails the next save's
    /// precondition (HTTP 412) even though nothing else touched the blob in between.</returns>
    Task<string?> SaveAsync(BookwormMemory memory, string? etag, CancellationToken ct = default);
}
