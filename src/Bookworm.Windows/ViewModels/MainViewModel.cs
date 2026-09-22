using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Bookworm.Windows.Services;

namespace Bookworm.Windows.ViewModels;

public sealed record TranscriptEntry(string Speaker, string Text);

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<TranscriptEntry> Transcript { get; } = [];

    private string _statusText = "Starting up…";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Speech/orchestrator callbacks can arrive on a background thread — always marshal to the UI thread
    // before touching bound properties/collections.
    public void AddTranscript(string text, bool isUser) =>
        Application.Current.Dispatcher.Invoke(() => Transcript.Add(new TranscriptEntry(isUser ? "You" : "Bookworm", text)));

    public void SetState(ConversationState state) =>
        Application.Current.Dispatcher.Invoke(() => StatusText = state switch
        {
            ConversationState.Listening => "Listening…",
            ConversationState.Thinking => "Working on it…",
            ConversationState.Speaking => "Speaking…",
            _ => "Ready. Press and hold Talk, or press Space or Enter, to speak.",
        });
}
