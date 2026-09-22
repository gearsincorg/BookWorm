namespace Bookworm.Core.Logging;

/// <summary>Appends to one rolling daily plain-text file under LocalApplicationData\Bookworm\logs.</summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public FileAppLogger(string? appDataOverride = null)
    {
        var baseDir = appDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(baseDir, "Bookworm", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} — {exception}");

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        var path = Path.Combine(_logDirectory, $"bookworm-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        lock (_writeLock)
        {
            try
            {
                File.AppendAllLines(path, [line]);
            }
            catch
            {
                // Logging must never be the reason the app crashes.
            }
        }
    }
}
