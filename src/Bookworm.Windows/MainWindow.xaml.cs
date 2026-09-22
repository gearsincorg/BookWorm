using System.Windows;
using System.Windows.Input;
using Bookworm.Windows.Services;
using Bookworm.Windows.ViewModels;

namespace Bookworm.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml. Push-to-talk works three ways: mouse press/release on the
/// Talk button (true press-and-hold), Space/Enter anywhere in the window regardless of which control
/// currently has keyboard focus (handled at the Window level via tunneling Preview events, not on the
/// button itself), or the global hotkey (Ctrl+Alt+B) from anywhere on the desktop — the latter two are a
/// press/press toggle rather than press-and-hold, since holding a key (or a system-wide hotkey, which has
/// no separate release event at all) down for a variable-length recording isn't practical.
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

    private async void TalkButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginTalk();

    private async void TalkButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await EndTalkAsync();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true; // intercept before it reaches whichever control has focus (e.g. the transcript list)

        if (e.IsRepeat)
        {
            return; // holding the key sends repeated KeyDown events — only the first one should act
        }

        await ToggleTalkAsync();
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            e.Handled = true;
        }
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
