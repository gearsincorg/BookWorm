using System.Runtime.Versioning;
using System.Speech.Recognition;
using Bookworm.Core.Speech;

namespace Bookworm.Windows.Platform.Speech;

/// <summary>
/// STT via the built-in Windows SAPI dictation engine (System.Speech) — free, offline, and the
/// simplest option to wire into push-to-talk. If real-world accuracy on book/author names proves
/// inadequate, the fallback per docs/decisions.md is Windows.Media.SpeechRecognition (WinRT), not a
/// paid cloud STT service, since Claude API is already the app's main ongoing cost.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    private readonly SpeechRecognitionEngine _engine;
    private TaskCompletionSource<string>? _pending;

    public SystemSpeechRecognizer()
    {
        try
        {
            _engine = new SpeechRecognitionEngine();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "No speech recognizer is installed for the current Windows speech language. " +
                "Check Settings > Time & Language > Speech.", ex);
        }

        _engine.SetInputToDefaultAudioDevice();
        _engine.LoadGrammar(new DictationGrammar());
        _engine.SpeechRecognized += (_, e) => _pending?.TrySetResult(e.Result?.Text ?? "");
        _engine.SpeechRecognitionRejected += (_, _) => _pending?.TrySetResult("");
        _engine.RecognizeCompleted += (_, e) => _pending?.TrySetResult(e.Result?.Text ?? "");
    }

    public void StartListening()
    {
        _pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.RecognizeAsync(RecognizeMode.Single);
    }

    public async Task<string> StopListeningAsync(CancellationToken ct = default)
    {
        if (_pending is null)
        {
            throw new InvalidOperationException("StartListening must be called before StopListeningAsync.");
        }

        _engine.RecognizeAsyncStop();
        await using var registration = ct.Register(() => _pending.TrySetCanceled(ct));
        return await _pending.Task;
    }

    public void Dispose() => _engine.Dispose();
}
