using System.Runtime.Versioning;
using System.Text;
using Bookworm.Core.Speech;
using Windows.Media.SpeechRecognition;

namespace Bookworm.Windows.Platform.Speech;

/// <summary>
/// STT via the modern Windows speech platform (Windows.Media.SpeechRecognition, WinRT) — the fallback
/// named in docs/decisions.md after legacy SAPI dictation (<see cref="SystemSpeechRecognizer"/>) proved
/// too inaccurate in real testing (garbled entire phrases, not just proper nouns). Uses a continuous
/// recognition session so start/stop map exactly to push-to-talk press/release, rather than relying on
/// WinRT's own single-shot auto-endpointing.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WinRtSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    private readonly SpeechRecognizer _recognizer = new();
    private readonly StringBuilder _accumulated = new();
    private bool _initialized;
    private Task? _startTask;

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        var compilationResult = await _recognizer.CompileConstraintsAsync();
        if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
        {
            throw new InvalidOperationException($"Failed to initialize Windows speech recognition: {compilationResult.Status}.");
        }

        _recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Result.Text))
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

        _initialized = true;
    }

    public void StartListening()
    {
        lock (_accumulated)
        {
            _accumulated.Clear();
        }
        // Tracked (not fire-and-forget) so StopListeningAsync can wait for start to actually finish and
        // surface any failure — calling StopAsync on a session that never finished starting throws an
        // opaque COMException that masks the real problem.
        _startTask = StartInternalAsync();
    }

    private async Task StartInternalAsync()
    {
        await EnsureInitializedAsync();
        await _recognizer.ContinuousRecognitionSession.StartAsync();
    }

    public async Task<string> StopListeningAsync(CancellationToken ct = default)
    {
        if (_startTask is not null)
        {
            await _startTask;
        }
        await _recognizer.ContinuousRecognitionSession.StopAsync();
        lock (_accumulated)
        {
            return _accumulated.ToString().Trim();
        }
    }

    public void Dispose() => _recognizer.Dispose();
}
