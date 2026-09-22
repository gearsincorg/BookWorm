namespace Bookworm.Core.Speech;

/// <summary>
/// Push-to-talk speech capture: <see cref="StartListening"/> on button/key press,
/// <see cref="StopListeningAsync"/> on release — matches the half-duplex, turn-based
/// interaction model (see docs/decisions.md), not a continuously-listening assistant.
/// </summary>
public interface ISpeechRecognizer : IDisposable
{
    void StartListening();

    /// <returns>The recognized text, or "" if nothing was understood.</returns>
    Task<string> StopListeningAsync(CancellationToken ct = default);
}
