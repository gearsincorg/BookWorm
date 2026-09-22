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

    public Task SpeakAsync(string text, CancellationToken ct = default) =>
        Task.Run(() => _synth.Speak(text), ct);

    public void Dispose() => _synth.Dispose();
}
