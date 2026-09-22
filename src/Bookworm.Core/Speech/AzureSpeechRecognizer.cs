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
    private readonly SpeechConfig _config;
    private readonly StringBuilder _accumulated = new();

    private SpeechRecognizer? _recognizer;
    private Task? _startTask;

    public AzureSpeechRecognizer(AzureSpeechCredentials credentials, string language = "en-AU")
    {
        _config = SpeechConfig.FromSubscription(credentials.Key, credentials.Region);
        _config.SpeechRecognitionLanguage = language;
    }

    public void StartListening()
    {
        lock (_accumulated)
        {
            _accumulated.Clear();
        }

        // A fresh SpeechRecognizer per turn, not a reused one — calling StartContinuousRecognitionAsync
        // again too soon after a StopContinuousRecognitionAsync on the *same* instance can race the SDK's
        // internal session teardown (SPXERR_START_RECOGNIZING_INVALID_STATE_TRANSITION), which only
        // surfaced once "barge in" made rapid stop-then-start-again a real path (see PushToTalkController).
        // Recreating the object sidesteps that race entirely rather than trying to time around it.
        _recognizer?.Dispose();
        var recognizer = new SpeechRecognizer(_config);
        recognizer.Recognized += (_, e) =>
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
        _recognizer = recognizer;

        // Tracked, not fire-and-forget — see WinRtSpeechRecognizer for why this ordering matters.
        _startTask = recognizer.StartContinuousRecognitionAsync();
    }

    public async Task<string> StopListeningAsync(CancellationToken ct = default)
    {
        var recognizer = _recognizer;
        if (recognizer is null)
        {
            return "";
        }

        if (_startTask is not null)
        {
            await _startTask;
        }
        await recognizer.StopContinuousRecognitionAsync();
        lock (_accumulated)
        {
            return _accumulated.ToString().Trim();
        }
    }

    public void Dispose() => _recognizer?.Dispose();
}
