using System.Media;
using Bookworm.Core.Logging;
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
/// from button press/release (mouse), a press/press toggle (keyboard), or the global hotkey — see
/// MainWindow.xaml.cs. This is the piece that turns the separately-built-and-tested Phase 1–3
/// components into one voice loop.
///
/// Supports "barge in": starting a new Talk while a previous turn is still thinking or speaking cancels
/// that turn (including cutting off TTS audio mid-sentence — see SapiSpeechSynthesizer) and immediately
/// starts listening for the new one, rather than blocking or queuing. LibrarianOrchestrator's turn gate
/// and rollback-on-failure handle the case where the cancelled turn was mid-way through mutating shared
/// conversation state.
/// </summary>
public sealed class PushToTalkController(ISpeechRecognizer recognizer, ISpeechSynthesizer synthesizer, LibrarianOrchestrator orchestrator, IAppLogger logger)
{
    public event Action<ConversationState>? StateChanged;
    public event Action<string, bool>? TranscriptAdded; // (text, isUser)

    private bool _isListening;
    private CancellationTokenSource? _currentTurnCts;

    public void StartListening()
    {
        if (_isListening)
        {
            return;
        }

        // Interrupt whatever the previous turn was doing (Thinking/Speaking) rather than blocking this
        // request — the previous turn's own cleanup is responsible for not touching UI state afterward.
        _currentTurnCts?.Cancel();

        _isListening = true;
        // The global hotkey can trigger this while the window isn't visible/focused, so an audible cue
        // is the only feedback that listening actually started — not just cosmetic.
        SystemSounds.Asterisk.Play();
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

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentTurnCts = cts;
        try
        {
            await ProcessAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.Info("Turn cancelled — a new one started before this one finished.");
            // No StateChanged/TranscriptAdded here: whichever newer turn caused the cancellation already
            // owns the UI from this point on. Firing more events here would stomp on its state.
        }
        finally
        {
            if (ReferenceEquals(_currentTurnCts, cts))
            {
                _currentTurnCts = null;
            }
            cts.Dispose();
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        SystemSounds.Beep.Play(); // confirms the stop registered, before the (sometimes multi-second) round trip
        var transcript = await recognizer.StopListeningAsync(ct);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            logger.Info("Turn ended with no recognized speech.");
            StateChanged?.Invoke(ConversationState.Idle);
            return;
        }

        logger.Info($"User: {transcript}");
        TranscriptAdded?.Invoke(transcript, true);
        StateChanged?.Invoke(ConversationState.Thinking);

        string response;
        try
        {
            response = await orchestrator.ProcessUserUtteranceAsync(transcript, ct);
            logger.Info($"Bookworm: {response}");
        }
        catch (OperationCanceledException)
        {
            throw; // let StopAndProcessAsync's catch handle it quietly — see comment there
        }
        catch (Exception ex)
        {
            // Never speak ex.Message directly — it can be an arbitrarily large technical blob (a raw
            // API error body did exactly this once). Full detail goes to the log for the developer only.
            logger.Error($"Failed processing utterance: {transcript}", ex);
            response = "Sorry, I ran into a technical problem with that. Could you try again?";
        }

        TranscriptAdded?.Invoke(response, false);
        StateChanged?.Invoke(ConversationState.Speaking);

        // Independent of each other — no reason to make the user wait for the memory write before hearing the reply.
        var speakTask = synthesizer.SpeakAsync(response, ct);
        var saveTask = orchestrator.SaveMemoryAsync(ct);
        try
        {
            await Task.WhenAll(speakTask, saveTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error("Failed to speak the reply and/or save memory.", ex);
        }

        StateChanged?.Invoke(ConversationState.Idle);
    }
}
