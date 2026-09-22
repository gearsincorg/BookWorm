namespace Bookworm.Core.Speech;

/// <summary>Text-to-speech — every spoken response the app gives goes through this.</summary>
public interface ISpeechSynthesizer
{
    Task SpeakAsync(string text, CancellationToken ct = default);
}
