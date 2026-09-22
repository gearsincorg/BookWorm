namespace Bookworm.Core.Logging;

/// <summary>
/// Deliberately minimal — this app has one real user who can't read logs themselves; the point is to
/// give the developer enough to diagnose "it didn't work" reports after the fact, not to build
/// observability infrastructure. See docs/decisions.md Phase 5.
/// </summary>
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
