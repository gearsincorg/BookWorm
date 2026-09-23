using Bookworm.Core.Auth;
using Microsoft.CognitiveServices.Speech;

namespace Bookworm.Core.Speech;

/// <summary>TTS via Azure AI Speech neural voices, using the same resource as <see cref="AzureSpeechRecognizer"/>.</summary>
public sealed class AzureSpeechSynthesizer : ISpeechSynthesizer, IDisposable
{
    private readonly SpeechSynthesizer _synth;

    public AzureSpeechSynthesizer(AzureSpeechCredentials credentials, string voice = "en-AU-NatashaNeural")
    {
        var config = SpeechConfig.FromSubscription(credentials.Key, credentials.Region);
        config.SpeechSynthesisVoiceName = voice;
        _synth = new SpeechSynthesizer(config);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // SpeakTextAsync can't be cancelled directly; StopSpeakingAsync cuts off audio mid-sentence for barge-in.
        using var registration = ct.Register(() => _ = _synth.StopSpeakingAsync());
        var result = await _synth.SpeakTextAsync(text);

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            throw new InvalidOperationException($"Azure TTS failed: {details.Reason} — {details.ErrorDetails}");
        }
    }

    public void Dispose() => _synth.Dispose();
}
