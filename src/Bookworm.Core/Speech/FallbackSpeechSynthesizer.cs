namespace Bookworm.Core.Speech;

/// <summary>Speaks via <paramref name="primary"/>; if it fails (e.g. offline), speaks the same text via <paramref name="fallback"/>.</summary>
public sealed class FallbackSpeechSynthesizer(ISpeechSynthesizer primary, ISpeechSynthesizer fallback, Action<Exception>? onPrimaryFailed = null) : ISpeechSynthesizer, IDisposable
{
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        try
        {
            await primary.SpeakAsync(text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onPrimaryFailed?.Invoke(ex);
            await fallback.SpeakAsync(text, ct);
        }
    }

    public void Dispose()
    {
        (primary as IDisposable)?.Dispose();
        (fallback as IDisposable)?.Dispose();
    }
}
