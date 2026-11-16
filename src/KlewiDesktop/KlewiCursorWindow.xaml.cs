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
    private double targetLeft;
    private double targetTop;
    private bool hasTarget;

    public KlewiCursorWindow()
    {
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Left = area.Left + area.Width * 0.35;
        Top = area.Top + area.Height * 0.35;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle,
                style | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
        };

        timer.Tick += MoveCursor;
        Closed += (_, _) => timer.Stop();
    }

    // x/y are screen coordinates for the cursor tip.
    public void MoveTo(double screenX, double screenY)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            return;

        var area = SystemParameters.WorkArea;
        targetLeft = Math.Clamp(screenX - 2, area.Left, area.Right - Width);
        targetTop = Math.Clamp(screenY - 2, area.Top, area.Bottom - Height);
        hasTarget = true;

        if (!timer.IsEnabled)
            timer.Start();
    }

    private void MoveCursor(object? sender, EventArgs e)
    {
        if (!hasTarget)
        {
            timer.Stop();
            return;
        }

        Left += (targetLeft - Left) * 0.22;
        Top += (targetTop - Top) * 0.22;

        if (Math.Abs(targetLeft - Left) < 0.6 && Math.Abs(targetTop - Top) < 0.6)
        {
            Left = targetLeft;
            Top = targetTop;
            hasTarget = false;
            timer.Stop();
        }
    }

    public void Stop()
    {
        hasTarget = false;
        timer.Stop();
    }
}
