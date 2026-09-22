namespace Bookworm.Core.Library;

public sealed class VaLibraryClientOptions
{
    public string BaseUrl { get; init; } = "https://my.visionaustralia.org";

    /// <summary>
    /// Identifies this app to VA's portal. Good-citizen practice for an undocumented API:
    /// no public API exists, so this client behaves like a single polite human user, not a scraper.
    /// </summary>
    public string UserAgent { get; init; } = "Bookworm/0.1 (personal accessibility assistant; contact via VA account)";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
