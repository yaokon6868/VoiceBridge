using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoiceBridge;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x80;
    private readonly SettingsStore _store;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval=TimeSpan.FromMilliseconds(350) };

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);

    public OverlayWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _store.Save(); };
        Closed += (_, _) => { _saveTimer.Stop(); SaveBounds(); _saveTimer.Stop(); _store.Save(); };
        var s = store.Value;
        Left = s.Left; Top = s.Top; Width = s.Width; Height = s.Height;
        ApplyAppearance();
        SourceInitialized += (_, _) => ApplyExtendedStyle();
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        MouseLeftButtonDown += (_, e) => { if (!s.MouseThrough && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
    }

    public void ApplyAppearance()
    {
        var s = _store.Value;
        Caption.FontSize = s.FontSize;
        Caption.Opacity = s.TextOpacity;
        Backdrop.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(Math.Clamp(s.BackgroundOpacity, 0, 1) * 255), 0, 0, 0));
        ApplyExtendedStyle();
    }

    public void SetCaption(string text) => Caption.Text = text;

    public void SetVisible(bool visible)
    {
        if (visible) { if (!IsVisible) Show(); Topmost = true; }
        else Hide();
    }

    private void ApplyExtendedStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow;
        style = _store.Value.MouseThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(handle, GwlExStyle, style);
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal) return;
        var s = _store.Value;
        s.Left = Left; s.Top = Top; s.Width = Width; s.Height = Height;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
