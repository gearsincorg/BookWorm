using System.Text;
using Bookworm.Core.Auth;
using Microsoft.CognitiveServices.Speech;

namespace Bookworm.Core.Speech;

/// <summary>
/// STT via Azure AI Speech — escalation point after both free Windows options (legacy SAPI dictation
/// and WinRT Windows.Media.SpeechRecognition) proved inadequate in real testing (whole phrases garbled,
/// not just proper nouns). Portable (not Windows-only), so this lives in Core and is directly reusable
/// by the future Android/MAUI app, unlike the SAPI/WinRT implementations in Bookworm.Windows.Platform.
/// </summary>
public sealed class AzureSpeechRecognizer : ISpeechRecognizer
{
    private readonly SpeechRecognizer _recognizer;
    private readonly StringBuilder _accumulated = new();
    private Task? _startTask;

    public AzureSpeechRecognizer(AzureSpeechCredentials credentials, string language = "en-AU")
    {
        var config = SpeechConfig.FromSubscription(credentials.Key, credentials.Region);
        config.SpeechRecognitionLanguage = language;
        _recognizer = new SpeechRecognizer(config);
        _recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                lock (_accumulated)
                {
                    if (_accumulated.Length > 0)
                    {
                        _accumulated.Append(' ');
                    }
                    _accumulated.Append(e.Result.Text);
                }
            }
        };
    }

    public void StartListening()
    {
        lock (_accumulated)
        {
            _accumulated.Clear();
        }
        // Tracked, not fire-and-forget — see WinRtSpeechRecognizer for why this ordering matters.
        _startTask = _recognizer.StartContinuousRecognitionAsync();
    }

    public async Task<string> StopListeningAsync(CancellationToken ct = default)
    {
        if (_startTask is not null)
        {
            await _startTask;
        }
        await _recognizer.StopContinuousRecognitionAsync();
        lock (_accumulated)
        {
            return _accumulated.ToString().Trim();
        }
    }

    public void Dispose() => _recognizer.Dispose();
}
