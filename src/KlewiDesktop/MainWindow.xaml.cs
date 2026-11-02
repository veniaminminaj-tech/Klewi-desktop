using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KlewiDesktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string? projectPath;
    private string apiKey = "";
    private string model = "gemini-2.5-flash";
    private bool busy;

    public MainWindow() { InitializeComponent(); LoadSettings(); UpdateStatus(); }

    private void Navigate(string page, string title, string subtitle)
    {
        ChatPage.Visibility = page == "chat" ? Visibility.Visible : Visibility.Collapsed;
        ProjectPage.Visibility = page == "project" ? Visibility.Visible : Visibility.Collapsed;
        TerminalPage.Visibility = page == "terminal" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
    }
    private void ChatNav_Click(object s, RoutedEventArgs e) => Navigate("chat", "AI Chat", "Ask about code, debug errors, and plan changes.");
    private void ProjectNav_Click(object s, RoutedEventArgs e) => Navigate("project", "Project", "Browse files in your workspace.");
    private void TerminalNav_Click(object s, RoutedEventArgs e) => Navigate("terminal", "Terminal", "Run a limited set of developer commands.");
    private void SettingsNav_Click(object s, RoutedEventArgs e) { ApiKeyBox.Password = apiKey; ModelCombo.SelectedIndex = model.Contains("pro") ? 1 : 0; Navigate("settings", "Settings", "Configure your AI provider."); }

    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KlewiDesktop", "settings.json");
    private void LoadSettings()
    {
        try {
            if (!File.Exists(SettingsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var enc = doc.RootElement.GetProperty("key").GetString();
            if (!string.IsNullOrEmpty(enc)) apiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser));
            if (doc.RootElement.TryGetProperty("model", out var m)) model = m.GetString() ?? model;
        } catch { apiKey = ""; }
    }
    private void SaveSettings_Click(object s, RoutedEventArgs e)
    {
        try {
            apiKey = ApiKeyBox.Password.Trim(); model = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gemini-2.5-flash";
            if (apiKey.Length == 0) { SettingsStatus.Text = "Enter an API key first."; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var enc = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { key = enc, model }));
            SettingsStatus.Text = "Saved securely for this Windows account."; UpdateStatus();
        } catch (Exception ex) { SettingsStatus.Text = "Save failed: " + ex.Message; }
    }
    private void UpdateStatus() => ProviderStatus.Text = string.IsNullOrWhiteSpace(apiKey) ? "● Provider not configured" : "● Gemini • " + model;

    private async void TestConnection_Click(object s, RoutedEventArgs e)
    {
        if (ApiKeyBox.Password.Length > 0) apiKey = ApiKeyBox.Password.Trim();
        model = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? model;
        if (apiKey.Length == 0) { SettingsStatus.Text = "Enter an API key first."; return; }
        SettingsStatus.Text = "Testing connection…";
        try { SettingsStatus.Text = "Connected: " + await AskGemini("Reply exactly: Klewi connection works."); }
        catch (Exception ex) { SettingsStatus.Text = "Connection failed: " + ex.Message; }
    }
    private async Task<string> AskGemini(string prompt)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var payload = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.3, maxOutputTokens = 2048 } };
        using var response = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gemini HTTP {(int)response.StatusCode}: {(body.Length > 600 ? body[..600] : body)}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "(Empty response)";
    }
    private TextBlock AddMessage(string who, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        stack.Children.Add(new TextBlock { Text = who, FontWeight = FontWeights.SemiBold, Foreground = who == "You" ? Brushes.DeepSkyBlue : Brushes.White, Margin = new Thickness(0,0,0,5) });
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(207,218,234)), LineHeight = 21 };
        stack.Children.Add(body); Messages.Children.Add(stack); ChatScroll.ScrollToEnd(); return body;
    }
    private async void Send_Click(object s, RoutedEventArgs e) => await SendMessage();
    private async void ChatInput_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await SendMessage(); } }
    private async Task SendMessage()
    {
        var prompt = ChatInput.Text.Trim(); if (prompt.Length == 0 || busy) return;
        if (apiKey.Length == 0) { AddMessage("Klewi", "Open Settings and configure a Gemini API key first."); return; }
        ChatInput.Clear(); AddMessage("You", prompt); var pending = AddMessage("Klewi", "Thinking…"); busy = true;
        try { pending.Text = await AskGemini(prompt + (projectPath is null ? "" : $"\nWorkspace: {projectPath}. Do not claim to have read files.")); }
        catch (Exception ex) { pending.Text = "Request failed: " + ex.Message; }
        finally { busy = false; ChatScroll.ScrollToEnd(); }
    }

    private void OpenProject_Click(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose project folder" };
        if (dialog.ShowDialog() != true) return;
        projectPath = dialog.FolderName; ProjectPathText.Text = projectPath; ProjectTree.Items.Clear();
        var root = new TreeViewItem { Header = Path.GetFileName(projectPath), IsExpanded = true }; ProjectTree.Items.Add(root);
        try { FillTree(root, projectPath, 0); } catch (Exception ex) { MessageBox.Show(ex.Message, "Klewi"); }
    }
    private static void FillTree(TreeViewItem node, string path, int depth)
    {
        if (depth >= 3) return;
        foreach (var dir in Directory.EnumerateDirectories(path).Where(x => !Path.GetFileName(x).StartsWith('.')).Take(60)) {
            var child = new TreeViewItem { Header = "📁 " + Path.GetFileName(dir) }; node.Items.Add(child);
            try { FillTree(child, dir, depth + 1); } catch { }
        }
        foreach (var file in Directory.EnumerateFiles(path).Take(100)) node.Items.Add(new TreeViewItem { Header = "📄 " + Path.GetFileName(file) });
    }

    private async void RunTerminal_Click(object s, RoutedEventArgs e) => await RunCommand();
    private async void TerminalInput_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await RunCommand(); } }
    private async Task RunCommand()
    {
        var cmd = TerminalInput.Text.Trim(); if (cmd.Length == 0) return; TerminalInput.Clear(); TerminalOutput.Text += $"\n> {cmd}\n";
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pwd", "dir", "ls", "dotnet", "python", "python3", "node", "npm", "git" };
        var words = SplitArgs(cmd); var exe = words[0].ToLowerInvariant();
        if (!allowed.Contains(exe)) { TerminalOutput.Text += "Blocked: command is not on the allowlist.\n"; return; }
        bool permitted = exe switch {
            "pwd" or "dir" or "ls" => words.Count == 1,
            "git" => cmd.Equals("git status", StringComparison.OrdinalIgnoreCase),
            "dotnet" => cmd.Equals("dotnet --info", StringComparison.OrdinalIgnoreCase) || cmd.Equals("dotnet --version", StringComparison.OrdinalIgnoreCase) || cmd.Equals("dotnet build", StringComparison.OrdinalIgnoreCase),
            "python" or "python3" or "node" => words.Count == 2 && (words[1] == "--version" || words[1] == "-V" || words[1] == "-v"),
            "npm" => cmd.Equals("npm --version", StringComparison.OrdinalIgnoreCase) || cmd.Equals("npm -v", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!permitted) { TerminalOutput.Text += "Blocked: only safe version checks, git status, and dotnet build are enabled.\n"; return; }
        try {
            var psi = new ProcessStartInfo { FileName = exe is "pwd" or "dir" or "ls" ? "powershell.exe" : exe, WorkingDirectory = projectPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            if (exe is "pwd" or "dir" or "ls") { psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(exe == "pwd" ? "Get-Location" : "Get-ChildItem -Name"); }
            else foreach (var arg in words.Skip(1)) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process.WaitForExitAsync(cts.Token);
            TerminalOutput.Text += await stdout + await stderr + $"\n[exit code {process.ExitCode}]\n";
        } catch (OperationCanceledException) { TerminalOutput.Text += "Command timed out after 20 seconds.\n"; }
          catch (Exception ex) { TerminalOutput.Text += "Error: " + ex.Message + "\n"; }
        TerminalScroll.ScrollToEnd();
    }
    private static List<string> SplitArgs(string value)
    {
        var parts = new List<string>(); var b = new StringBuilder(); bool quote = false;
        foreach (char c in value) { if (c == '"') { quote = !quote; continue; } if (char.IsWhiteSpace(c) && !quote) { if (b.Length > 0) { parts.Add(b.ToString()); b.Clear(); } } else b.Append(c); }
        if (b.Length > 0) parts.Add(b.ToString()); return parts;
    }
}
