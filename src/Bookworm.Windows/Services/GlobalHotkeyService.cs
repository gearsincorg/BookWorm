using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Bookworm.Windows.Services;

/// <summary>
/// A system-wide hotkey via the Win32 RegisterHotKey API, so Talk can be triggered without the Bookworm
/// window being focused or even visible — the point of the father's request to "trigger it from
/// anywhere". RegisterHotKey only fires once per logical press (no separate release event the way a
/// normal key does), so the caller must treat this as a toggle, same as the Space/Enter keyboard path.
///
/// Unlike WM_KEYDOWN, a WM_HOTKEY message carries no repeat-count/flag to distinguish a fresh press from
/// OS-level key-repeat — simply holding the combo down for a moment fires several WM_HOTKEY messages in
/// a row, which would toggle start/stop/start/stop faster than the speech pipeline can settle between
/// them. A short debounce window filters that out.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(600);

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private readonly HwndSource _source;
    private readonly int _id;
    private bool _registered;
    private DateTimeOffset _lastTriggerAt = DateTimeOffset.MinValue;

    public event Action? HotkeyPressed;

    public GlobalHotkeyService(Window window, uint modifiers, uint virtualKey, int id = 0x9000)
    {
        _id = id;
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Window handle isn't ready yet — register the hotkey from OnSourceInitialized.");
        _source.AddHook(WndProc);

        if (!RegisterHotKey(handle, _id, modifiers, virtualKey))
        {
            throw new InvalidOperationException("Couldn't register the global hotkey — it may already be in use by another application.");
        }
        _registered = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastTriggerAt >= DebounceWindow)
            {
                _lastTriggerAt = now;
                HotkeyPressed?.Invoke();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _id);
            _registered = false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
