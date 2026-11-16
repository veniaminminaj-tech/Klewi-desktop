using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KlewiDesktop;

public partial class KlewiCursorWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double targetX, targetY;
    private bool hasTarget;

    public KlewiCursorWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Left = area.Left + area.Width * 0.5;
        Top = area.Top + area.Height * 0.5;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
        };
        timer.Tick += MoveCursor;
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    // Coordinates are physical desktop coordinates in WPF device-independent pixels.
    // This window is click-through and never sends mouse input.
    public void MoveTo(double screenX, double screenY)
    {
        var area = SystemParameters.WorkArea;
        targetX = Math.Clamp(screenX, area.Left, area.Right - Width);
        targetY = Math.Clamp(screenY, area.Top, area.Bottom - Height);
        hasTarget = true;
    }

    public void HideCursor() => Visibility = Visibility.Hidden;
    public void ShowCursor() => Visibility = Visibility.Visible;
    public void Stop() => timer.Stop();

    private void MoveCursor(object? sender, EventArgs e)
    {
        if (!hasTarget || !IsVisible) return;
        const double easing = 0.18;
        var dx = targetX - Left;
        var dy = targetY - Top;
        if (Math.Abs(dx) < 0.7 && Math.Abs(dy) < 0.7)
        {
            Left = targetX;
            Top = targetY;
            return;
        }
        Left += dx * easing;
        Top += dy * easing;
    }
}