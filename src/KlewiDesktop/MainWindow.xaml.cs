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
    private string provider = "Gemini";
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
    private void SettingsNav_Click(object s, RoutedEventArgs e) { ApiKeyBox.Password = apiKey; ProviderCombo.SelectedIndex = provider switch { "OpenAI" => 1, "OpenRouter" => 2, _ => 0 }; ModelBox.Text = model; Navigate("settings", "Settings", "Configure your AI provider."); }
    private void ProviderCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ModelBox is null || ProviderCombo.SelectedItem is not ComboBoxItem item) return;
        var selected = item.Content?.ToString() ?? "Gemini";
        if (provider == selected) return;
        ModelBox.Text = selected switch { "OpenAI" => "gpt-4.1-mini", "OpenRouter" => "openai/gpt-4.1-mini", _ => "gemini-2.5-flash" };
    }

    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KlewiDesktop", "settings.json");
    private void LoadSettings()
    {
        try {
            if (!File.Exists(SettingsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var enc = doc.RootElement.GetProperty("key").GetString();
            if (!string.IsNullOrEmpty(enc)) apiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser));
            if (doc.RootElement.TryGetProperty("model", out var m)) model = m.GetString() ?? model;
            if (doc.RootElement.TryGetProperty("provider", out var p)) provider = p.GetString() ?? provider;
        } catch { apiKey = ""; }
    }
    private void SaveSettings_Click(object s, RoutedEventArgs e)
    {
        try {
            apiKey = ApiKeyBox.Password.Trim(); provider = (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Gemini"; model = ModelBox.Text.Trim();
            if (model.Length == 0) { SettingsStatus.Text = "Enter a model ID."; return; }
            if (apiKey.Length == 0) { SettingsStatus.Text = "Enter an API key first."; return; }
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var enc = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser));
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { key = enc, model, provider }));
            SettingsStatus.Text = "Saved securely for this Windows account."; UpdateStatus();
        } catch (Exception ex) { SettingsStatus.Text = "Save failed: " + ex.Message; }
    }
    private void UpdateStatus() => ProviderStatus.Text = string.IsNullOrWhiteSpace(apiKey) ? "● Provider not configured" : "● " + provider + " • " + model;

    private async void TestConnection_Click(object s, RoutedEventArgs e)
    {
        if (ApiKeyBox.Password.Length > 0) apiKey = ApiKeyBox.Password.Trim();
        provider = (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? provider;
        model = ModelBox.Text.Trim();
        if (apiKey.Length == 0) { SettingsStatus.Text = "Enter an API key first."; return; }
        if (model.Length == 0) { SettingsStatus.Text = "Enter a model ID."; return; }
        SettingsStatus.Text = "Testing connection…";
        try { SettingsStatus.Text = "Connected: " + (await AskProvider("Reply exactly: Klewi connection works.")).Trim(); }
        catch (Exception ex) { SettingsStatus.Text = "Connection failed: " + ex.Message; }
    }
    private async Task<string> AskProvider(string prompt)
    {
        if (provider == "Gemini")
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var payload = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.3, maxOutputTokens = 4096 } };
            using var response = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gemini HTTP {(int)response.StatusCode}: {(body.Length > 600 ? body[..600] : body)}");
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                throw new InvalidOperationException("Gemini returned no candidates. Check model access, prompt safety, and API quota.");
            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            var textParts = parts.EnumerateArray().Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString()).Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(Environment.NewLine, textParts) is { Length: > 0 } answer ? answer : "(Empty response)";
        }

        var endpoint = provider == "OpenRouter" ? "https://openrouter.ai/api/v1/chat/completions" : "https://api.openai.com/v1/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        if (provider == "OpenRouter")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/veniaminminaj-tech/Klewi-desktop");
            request.Headers.TryAddWithoutValidation("X-Title", "Klewi Desktop");
        }
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            model,
            messages = new[] { new { role = "system", content = "You are Klewi, a helpful coding assistant. Be honest about actions: you cannot access or modify local files unless a tool explicitly provides them. Explain changes clearly." }, new { role = "user", content = prompt } },
            temperature = 0.3,
            max_tokens = 4096
        }), Encoding.UTF8, "application/json");
        using var compatibleResponse = await Http.SendAsync(request);
        var compatibleBody = await compatibleResponse.Content.ReadAsStringAsync();
        if (!compatibleResponse.IsSuccessStatusCode) throw new InvalidOperationException($"{provider} HTTP {(int)compatibleResponse.StatusCode}: {(compatibleBody.Length > 600 ? compatibleBody[..600] : compatibleBody)}");
        using var compatibleDoc = JsonDocument.Parse(compatibleBody);
        return compatibleDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "(Empty response)";
    }

    private async void LoadModels_Click(object s, RoutedEventArgs e)
    {
        if ((ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() != "OpenRouter")
        { SettingsStatus.Text = "Select OpenRouter to load its public model catalog."; return; }
        SettingsStatus.Text = "Loading model catalog…";
        try
        {
            using var response = await Http.GetAsync("https://openrouter.ai/api/v1/models");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var names = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Take(500).ToList();
            var picker = new Window { Title = "OpenRouter models", Width = 520, Height = 620, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Panel") };
            var panel = new DockPanel { Margin = new Thickness(12) };
            var search = new TextBox { Margin = new Thickness(0,0,0,10), ToolTip = "Filter model IDs" };
            DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
            var list = new ListBox { ItemsSource = names, Background = Brushes.Transparent, Foreground = Brushes.White };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is string chosen) { ModelBox.Text = chosen; picker.DialogResult = true; } };
            panel.Children.Add(list); picker.Content = panel;
            search.TextChanged += (_, _) => list.ItemsSource = names.Where(n => n!.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            picker.ShowDialog(); SettingsStatus.Text = "Choose a model and save settings.";
        }
        catch (Exception ex) { SettingsStatus.Text = "Could not load model catalog: " + ex.Message; }
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
        try { pending.Text = await AskProvider(prompt + (projectPath is null ? "" : $"\nWorkspace: {projectPath}. Do not claim to have read files.")); }
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
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
