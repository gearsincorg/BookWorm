namespace Bookworm.Core.Library.Exceptions;

/// <summary>Login to the VA portal failed (bad credentials, or the portal rejected the request).</summary>
public sealed class VaAuthenticationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The session cookie is no longer valid (expired, or was never established). Thrown by the raw
/// <see cref="Bookworm.Core.Library.VaLibraryClient"/> so <see cref="Bookworm.Core.Auth.VaSessionManager"/>
/// can catch it, re-authenticate once, and retry.
/// </summary>
public sealed class VaSessionExpiredException(string message = "VA session expired") : Exception(message);

/// <summary>Attempted to add a title to the bookshelf when the 20-item loan cap is already reached.</summary>
public sealed class VaLoanCapExceededException(int currentCount, int cap)
    : Exception($"Bookshelf loan cap reached ({currentCount}/{cap}). Use the request list instead.")
{
    public int CurrentCount { get; } = currentCount;
    public int Cap { get; } = cap;
}

/// <summary>A VA portal request failed for a reason other than session expiry (HTTP error, unexpected response shape).</summary>
public sealed class VaRequestFailedException(string message, int? statusCode = null, string? responseBody = null)
    : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
}
