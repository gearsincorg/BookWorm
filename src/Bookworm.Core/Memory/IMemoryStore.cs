namespace Bookworm.Core.Memory;

public sealed record MemoryLoadResult(BookwormMemory Memory, string? ETag);

public interface IMemoryStore
{
    Task<MemoryLoadResult> LoadAsync(CancellationToken ct = default);

    /// <param name="etag">Pass the ETag from <see cref="LoadAsync"/> for optimistic concurrency; null to
    /// write unconditionally (first save, or when overwriting is intentional).</param>
    Task SaveAsync(BookwormMemory memory, string? etag, CancellationToken ct = default);
}
