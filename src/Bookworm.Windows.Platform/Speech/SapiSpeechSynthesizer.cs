using System.Runtime.Versioning;
using System.Speech.Synthesis;
using Bookworm.Core.Speech;

namespace Bookworm.Windows.Platform.Speech;

/// <summary>
/// TTS via the built-in Windows SAPI synthesizer (System.Speech) — free, offline, simplest to wire up.
/// Per docs/decisions.md, response quality matters disproportionately here since the user only ever
/// hears the answer; if legacy SAPI voices prove too robotic in practice, upgrade to WinRT's "Natural"
/// voices (Windows.Media.SpeechSynthesis) behind this same <see cref="ISpeechSynthesizer"/> interface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapiSpeechSynthesizer : ISpeechSynthesizer, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public SapiSpeechSynthesizer() => _synth.SetOutputToDefaultAudioDevice();

    // Uses the real async Speak API (not Speak() wrapped in Task.Run) specifically so cancellation can
    // stop the audio mid-sentence via SpeakAsyncCancelAll — a Task.Run wrapper only checks the token
    // before starting, so once synchronous Speak() began playing, cancelling the Task would do nothing
    // to the actual sound (this mattered once "barge in" — talking over a reply to ask something new —
    // became a real requirement, not just cancelling the underlying orchestration/network call).
    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;

        void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            _synth.SpeakCompleted -= OnSpeakCompleted;
            registration.Dispose();
            if (e.Cancelled)
            {
                tcs.TrySetCanceled(ct);
            }
            else if (e.Error is not null)
            {
                tcs.TrySetException(e.Error);
            }
            else
            {
                tcs.TrySetResult();
            }
        }

        _synth.SpeakCompleted += OnSpeakCompleted;
        registration = ct.Register(() => _synth.SpeakAsyncCancelAll());
        _synth.SpeakAsync(text);
        return tcs.Task;
    }

    public void Dispose() => _synth.Dispose();
}
