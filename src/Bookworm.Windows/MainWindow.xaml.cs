using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Bookworm.Windows.Services;
using Bookworm.Windows.ViewModels;

namespace Bookworm.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml. Push-to-talk works three ways:
/// - Mouse press/release on the Talk button (true press-and-hold).
/// - Space/Enter anywhere in the window (true press-and-hold too — WPF delivers real KeyDown/KeyUp
///   pairs for a focused-window key, so this mirrors the mouse exactly), regardless of which control
///   currently has focus (handled at the Window level via tunneling Preview events, not on the button).
/// - The global hotkey (Ctrl+Alt+B) from anywhere on the desktop — this one is a press/press *toggle*,
///   not press-and-hold, because Win32's RegisterHotKey has no separate release event at all to hold
///   against; that's an OS API limitation, not a design choice.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PushToTalkController _controller;
    private bool _isActive;
    private GlobalHotkeyService? _hotkey;

    public MainWindow(MainViewModel viewModel, PushToTalkController controller)
    {
        InitializeComponent();
        _controller = controller;
        DataContext = viewModel;

        _controller.StateChanged += viewModel.SetState;
        _controller.TranscriptAdded += viewModel.AddTranscript;

        // Keep the transcript scrolled to the newest entry — otherwise it just grows downward off-screen.
        // ScrollIntoView has to run after layout has caught up with the just-added item (it's raised
        // synchronously from Add, before WPF has generated a container for the new item), so this is
        // deferred to ContextIdle rather than called directly in the handler.
        viewModel.Transcript.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
            {
                var newest = e.NewItems[^1]!;
                Dispatcher.BeginInvoke(() => TranscriptListBox.ScrollIntoView(newest), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hotkey = new GlobalHotkeyService(this, GlobalHotkeyService.ModControl | GlobalHotkeyService.ModAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.B));
            _hotkey.HotkeyPressed += async () => await ToggleTalkAsync();
        }
        catch (Exception ex)
        {
            // Non-fatal: the Talk button and Space/Enter still work without the global hotkey.
            MessageBox.Show(
                $"Couldn't set up the Ctrl+Alt+B shortcut ({ex.Message}). The Talk button still works normally.",
                "Bookworm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        base.OnClosed(e);
    }

    private void TalkButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginTalk();

    private async void TalkButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await EndTalkAsync();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true; // intercept before it reaches whichever control has focus (e.g. the transcript list)

        if (e.IsRepeat)
        {
            return; // holding the key sends repeated KeyDown events — only the first counts as "pressed"
        }

        BeginTalk();
    }

    private async void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true;
        await EndTalkAsync();
    }

    private async Task ToggleTalkAsync()
    {
        if (_isActive)
        {
            await EndTalkAsync();
        }
        else
        {
            BeginTalk();
        }
    }

    private void BeginTalk()
    {
        if (_isActive)
        {
            return;
        }
        _isActive = true;
        _controller.StartListening();
    }

    private async Task EndTalkAsync()
    {
        if (!_isActive)
        {
            return;
        }
        _isActive = false;
        await _controller.StopAndProcessAsync();
    }
}
