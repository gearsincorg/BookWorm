using System.Windows;
using System.Windows.Input;
using Bookworm.Windows.Services;
using Bookworm.Windows.ViewModels;

namespace Bookworm.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml. Push-to-talk works two ways: mouse press/release on the Talk
/// button (true press-and-hold), or Space/Enter while it's focused (a press/press toggle, since holding
/// a key down for a variable-length recording is awkward for keyboard-only/screen-reader users).
/// </summary>
public partial class MainWindow : Window
{
    private readonly PushToTalkController _controller;
    private bool _isActive;

    public MainWindow(MainViewModel viewModel, PushToTalkController controller)
    {
        InitializeComponent();
        _controller = controller;
        DataContext = viewModel;

        _controller.StateChanged += viewModel.SetState;
        _controller.TranscriptAdded += viewModel.AddTranscript;
    }

    private void TalkButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginTalk();

    private async void TalkButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await EndTalkAsync();

    private async void TalkButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true; // suppress the button's own Click so it doesn't fire alongside our toggle

        if (e.IsRepeat)
        {
            return; // holding the key sends repeated KeyDown events — only the first one should act
        }

        if (_isActive)
        {
            await EndTalkAsync();
        }
        else
        {
            BeginTalk();
        }
    }

    private void TalkButton_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            e.Handled = true;
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
