using Bookworm.Core.Orchestration;
using Bookworm.Core.Speech;

namespace Bookworm.Windows.Services;

public enum ConversationState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
}

/// <summary>
/// Wires the speech pipeline to the conversational Brain behind a simple start/stop API the UI drives
/// from button press/release (mouse) or a press/press toggle (keyboard) — see MainWindow.xaml.cs. This
/// is the piece that turns the separately-built-and-tested Phase 1–3 components into one voice loop.
/// </summary>
public sealed class PushToTalkController(ISpeechRecognizer recognizer, ISpeechSynthesizer synthesizer, LibrarianOrchestrator orchestrator)
{
    public event Action<ConversationState>? StateChanged;
    public event Action<string, bool>? TranscriptAdded; // (text, isUser)

    private bool _isListening;

    public void StartListening()
    {
        if (_isListening)
        {
            return;
        }
        _isListening = true;
        recognizer.StartListening();
        StateChanged?.Invoke(ConversationState.Listening);
    }

    public async Task StopAndProcessAsync(CancellationToken ct = default)
    {
        if (!_isListening)
        {
            return;
        }
        _isListening = false;

        var transcript = await recognizer.StopListeningAsync(ct);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            StateChanged?.Invoke(ConversationState.Idle);
            return;
        }

        TranscriptAdded?.Invoke(transcript, true);
        StateChanged?.Invoke(ConversationState.Thinking);

        string response;
        try
        {
            response = await orchestrator.ProcessUserUtteranceAsync(transcript, ct);
        }
        catch (Exception ex)
        {
            response = $"Sorry, something went wrong: {ex.Message}";
        }

        TranscriptAdded?.Invoke(response, false);
        StateChanged?.Invoke(ConversationState.Speaking);

        // Independent of each other — no reason to make the user wait for the memory write before hearing the reply.
        var speakTask = synthesizer.SpeakAsync(response, ct);
        var saveTask = orchestrator.SaveMemoryAsync(ct);
        await Task.WhenAll(speakTask, saveTask);

        StateChanged?.Invoke(ConversationState.Idle);
    }
}
