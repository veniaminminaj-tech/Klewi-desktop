using System;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(28) };
    private readonly Random random = new();
    private double targetX, targetY;
    private double speed = 3.2;

    public KlewiCursorWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Left = area.Left + area.Width * 0.35;
        Top = area.Top + area.Height * 0.35;
        ChooseTarget();
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

    private void ChooseTarget()
    {
        var area = SystemParameters.WorkArea;
        targetX = area.Left + 40 + random.NextDouble() * Math.Max(1, area.Width - 100);
        targetY = area.Top + 40 + random.NextDouble() * Math.Max(1, area.Height - 100);
        speed = 2.0 + random.NextDouble() * 2.5;
    }

    private void MoveCursor(object? sender, EventArgs e)
    {
        var dx = targetX - Left;
        var dy = targetY - Top;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 12)
        {
            ChooseTarget();
            return;
        }
        Left += dx / distance * speed;
        Top += dy / distance * speed;
    }

    public void Stop() => timer.Stop();
}