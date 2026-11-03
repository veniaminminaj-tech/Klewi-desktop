using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KlewiDesktop;

public partial class MascotWindow : Window
{
    private readonly DispatcherTimer movementTimer;
    private readonly DispatcherTimer speechTimer;
    private readonly Random random = new();
    private readonly Action openMain;
    private double velocityX = 1.15;
    private double velocityY = 0.0;
    private bool dragging;
    private Point dragStart;
    private Point windowStart;
    private bool paused;
    private int speechIndex;

    private readonly string[] idleMessages =
    {
        "Я тут, якщо потрібна допомога!",
        "Працюємо над кодом? ✦",
        "Можеш перетягнути мене мишкою.",
        "Подвійний клік — відкрию чат!",
        "Не забудь зберегти проєкт."
    };

    public MascotWindow(Action openMain)
    {
        InitializeComponent();
        this.openMain = openMain;
        Left = SystemParameters.WorkArea.Right - 165;
        Top = SystemParameters.WorkArea.Bottom - 220;

        movementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        movementTimer.Tick += MovePet;
        movementTimer.Start();

        speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
        speechTimer.Tick += (_, _) =>
        {
            if (!dragging) Say(idleMessages[speechIndex++ % idleMessages.Length]);
        };
        speechTimer.Start();

        Loaded += (_, _) => Say("Привіт! Я Клеві.");
        Closed += (_, _) => { movementTimer.Stop(); speechTimer.Stop(); };
    }

    public void Say(string message)
    {
        SpeechText.Text = message;
        Bubble.Visibility = Visibility.Visible;
    }

    private void MovePet(object? sender, EventArgs e)
    {
        if (paused || dragging || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0) return;

        var area = SystemParameters.WorkArea;
        var maxX = Math.Max(area.Left, area.Right - ActualWidth);
        var maxY = Math.Max(area.Top, area.Bottom - ActualHeight);

        if (random.NextDouble() < 0.006)
        {
            velocityX = (random.NextDouble() < 0.5 ? -1 : 1) * (0.65 + random.NextDouble() * 1.1);
            velocityY = (random.NextDouble() - 0.5) * 0.55;
        }

        Left += velocityX;
        Top += velocityY;

        if (Left <= area.Left || Left >= maxX) velocityX *= -1;
        if (Top <= area.Top || Top >= maxY) velocityY *= -1;
        Left = Math.Clamp(Left, area.Left, maxX);
        Top = Math.Clamp(Top, area.Top, maxY);
    }

    private void PetCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            openMain();
            e.Handled = true;
            return;
        }

        dragging = true;
        dragStart = e.GetPosition(null);
        windowStart = new Point(Left, Top);
        PetCanvas.CaptureMouse();
        Say("Пересувай мене!");
    }

    private void PetCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(null);
        Left = windowStart.X + point.X - dragStart.X;
        Top = windowStart.Y + point.Y - dragStart.Y;
    }

    private void PetCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        PetCanvas.ReleaseMouseCapture();
        Say("Ось моє нове місце!");
    }

    private void PetCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowPetMenu();
        e.Handled = true;
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e) => ShowPetMenu();

    private void ShowPetMenu()
    {
        var menu = new ContextMenu();
        var chat = new MenuItem { Header = "Відкрити чат" };
        chat.Click += (_, _) => openMain();
        var say = new MenuItem { Header = "Сказати щось" };
        say.Click += (_, _) => Say(idleMessages[random.Next(idleMessages.Length)]);
        var pause = new MenuItem { Header = paused ? "Продовжити рух" : "Зупинити рух" };
        pause.Click += (_, _) => { paused = !paused; Say(paused ? "Я відпочиваю." : "Знову рухаюсь!"); };
        var hide = new MenuItem { Header = "Сховати Клеві" };
        hide.Click += (_, _) => Hide();
        var exit = new MenuItem { Header = "Закрити Клеві" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(chat);
        menu.Items.Add(say);
        menu.Items.Add(new Separator());
        menu.Items.Add(pause);
        menu.Items.Add(hide);
        menu.Items.Add(exit);
        menu.IsOpen = true;
    }

    public void ShowPet()
    {
        if (!IsVisible) Show();
        Activate();
    }
}