using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KlewiDesktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string? projectPath;
    private string? selectedFilePath;
    private string pendingFileContext = "";
    private string apiKey = "";
    private string model = "gemini-2.5-flash";
    private string provider = "Gemini";
    private string customInstructions = "";
    private double chatTemperature = 0.3;
    private int chatMaxTokens = 4096;
    private string minecraftDefaultVersion = "1.21.1";
    private string minecraftDefaultLoader = "Fabric";
    private string minecraftDefaultTask = "Minecraft mod (Java)";
    private string minecraftOutputFolder = "";
    private string? lastMinecraftProjectDirectory;
    private string? selectedIsoPath;
    private string? mountedIsoPath;
    private string? mountedIsoDrive;
    private string loadedFileContents = "";
    private bool busy;

    public MainWindow() { InitializeComponent(); LoadSettings(); UpdateStatus(); }

    private void Navigate(string page, string title, string subtitle)
    {
        ChatPage.Visibility = page == "chat" ? Visibility.Visible : Visibility.Collapsed;
        ProjectPage.Visibility = page == "project" ? Visibility.Visible : Visibility.Collapsed;
        MinecraftPage.Visibility = page == "minecraft" ? Visibility.Visible : Visibility.Collapsed;
        IsoPage.Visibility = page == "iso" ? Visibility.Visible : Visibility.Collapsed;
        TerminalPage.Visibility = page == "terminal" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
    }
    private void ChatNav_Click(object s, RoutedEventArgs e) => Navigate("chat", "AI Chat", "Ask about code, debug errors, and plan changes.");
    private void ProjectNav_Click(object s, RoutedEventArgs e) => Navigate("project", "Project", "Browse files in your workspace.");
    private void TerminalNav_Click(object s, RoutedEventArgs e) => Navigate("terminal", "Terminal", "Run a limited set of developer commands.");
    private void MinecraftNav_Click(object s, RoutedEventArgs e) => Navigate("minecraft", "Minecraft Studio", "Create mod and resource-pack plans with AI.");
    private void IsoNav_Click(object s, RoutedEventArgs e) => Navigate("iso", "ISO Tools", "Inspect and mount Windows installation images.");

    private void ChooseMinecraftOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where Minecraft projects will be saved" };
        var current = MinecraftOutputPathBox.Text.Trim();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog() == true) MinecraftOutputPathBox.Text = dialog.FolderName;
    }

    private async void GenerateMinecraft_Click(object sender, RoutedEventArgs e)
    {
        var task = (MinecraftTaskCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Minecraft mod (Java)";
        var loader = (MinecraftLoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Fabric";
        var version = MinecraftVersionBox.Text.Trim();
        var name = MinecraftNameBox.Text.Trim();
        var idea = MinecraftIdeaBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MinecraftStatusText.Text = "Configure an AI provider in Settings first.";
            return;
        }
        if (version.Length == 0 || name.Length == 0 || idea.Length == 0)
        {
            MinecraftStatusText.Text = "Fill in the Minecraft version, project name, and idea.";
            return;
        }
        if (busy) return;

        var basePrompt = string.Join(Environment.NewLine, new[]
        {
            "You are an expert Minecraft Java developer and resource-pack creator.",
            "Return exactly one valid JSON object. Do not use Markdown fences and do not add prose before or after the JSON.",
            "All file contents must be complete, consistent, and ready to save. Do not use placeholders such as TODO or 'rest of code here'.",
            "Use only APIs compatible with the exact Minecraft version and selected loader. Never invent APIs. If uncertain, state that in the summary and avoid fabricating compatibility.",
            $"Project name: {name}",
            $"Minecraft version: {version}",
            $"Loader: {loader}",
            $"Requested output type: {task}",
            $"User idea: {idea}"
        });

        string prompt;
        if (task == "Texture design (pixel art)")
        {
            prompt = basePrompt + Environment.NewLine +
                "Return JSON keys: kind (must be 'texture'), summary, assetPath, width, height, palette, pixels. " +
                "width and height must each be exactly 16 or 32. palette must be an array of 1 to 64 hex colors such as #RRGGBB or #AARRGGBB. " +
                "pixels must be an array of exactly height rows; every row must be an array of exactly width integer palette indexes starting at zero. " +
                "Every pixel index must reference an existing palette entry. assetPath must be a safe relative Minecraft asset path ending in .png. " +
                "Do not include any other output format.";
        }
        else
        {
            prompt = basePrompt + Environment.NewLine +
                "Return JSON keys: kind (must be 'project'), summary, files. files must be an array of objects, each with path and content strings. " +
                "Include every file needed for a minimal complete project: Gradle build files, settings, loader metadata, Java source, resources, assets, and a README as appropriate. " +
                "For a resource pack include pack.mcmeta, required model JSON, and asset files. For a mod include correct loader metadata, mod entry point, registrations, and resources. " +
                "Use forward-slash relative paths. Never include absolute paths, '..' path segments, binary data, or Markdown fences. " +
                "Keep each file content as plain text. Do not put PNG binary content in files; use the texture mode for PNG output.";
        }

        busy = true;
        GenerateMinecraftButton.IsEnabled = false;
        MinecraftStatusText.Text = "Asking " + provider + " to generate the project…";
        try
        {
            var response = await AskProvider(prompt);
            using var document = JsonDocument.Parse(ExtractJsonObject(response));
            var root = document.RootElement;
            var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() ?? "" : "";
            var basePath = MinecraftOutputPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathRooted(basePath))
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KlewiMinecraft");
            Directory.CreateDirectory(basePath);

            var outputName = MakeSafeFileName(name);
            var outputDirectory = Path.Combine(basePath, outputName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(outputDirectory);
            lastMinecraftProjectDirectory = outputDirectory;
            MinecraftTexturePreview.Source = null;
            MinecraftTexturePreview.Visibility = Visibility.Collapsed;
            MinecraftBuildOutput.Clear();

            if (task == "Texture design (pixel art)")
            {
                if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "texture")
                    throw new InvalidDataException("AI response is not a texture manifest.");
                var relativeAssetPath = root.GetProperty("assetPath").GetString() ?? "textures/generated.png";
                var texturePath = GetSafeOutputPath(outputDirectory, relativeAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
                WriteTexturePng(root, texturePath);
                var previewBitmap = new BitmapImage();
                previewBitmap.BeginInit();
                previewBitmap.CacheOption = BitmapCacheOption.OnLoad;
                previewBitmap.UriSource = new Uri(texturePath);
                previewBitmap.EndInit();
                previewBitmap.Freeze();
                MinecraftTexturePreview.Source = previewBitmap;
                MinecraftTexturePreview.Visibility = Visibility.Visible;
                File.WriteAllText(Path.Combine(outputDirectory, "README.txt"),
                    "Generated Minecraft texture" + Environment.NewLine +
                    "Asset path: " + relativeAssetPath + Environment.NewLine +
                    "Minecraft version: " + version + Environment.NewLine +
                    "Description: " + idea + Environment.NewLine + summary);
            }
            else
            {
                if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "project")
                    throw new InvalidDataException("AI response is not a project manifest.");
                var files = root.GetProperty("files");
                if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0 || files.GetArrayLength() > 150)
                    throw new InvalidDataException("The generated project must contain between 1 and 150 files.");

                long totalBytes = 0;
                foreach (var file in files.EnumerateArray())
                {
                    var relative = file.GetProperty("path").GetString() ?? "";
                    var content = file.GetProperty("content").GetString() ?? "";
                    var target = GetSafeOutputPath(outputDirectory, relative);
                    var bytes = Encoding.UTF8.GetByteCount(content);
                    totalBytes += bytes;
                    if (bytes > 2 * 1024 * 1024 || totalBytes > 15 * 1024 * 1024)
                        throw new InvalidDataException("Generated project is too large (limit: 2 MB per file, 15 MB total).");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, content, new UTF8Encoding(false));
                }
                var readme = Path.Combine(outputDirectory, "KLEWI-GENERATED.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, "Generated by Klewi Desktop Minecraft Studio." + Environment.NewLine +
                        "Minecraft: " + version + Environment.NewLine + "Loader: " + loader + Environment.NewLine +
                        "Review source files and verify loader/version compatibility before building.");
            }

            var zipPath = outputDirectory + ".zip";
            ZipFile.CreateFromDirectory(outputDirectory, zipPath, CompressionLevel.Optimal, false);
            MinecraftStatusText.Text = "Created: " + outputDirectory + Environment.NewLine + "ZIP: " + zipPath +
                (string.IsNullOrWhiteSpace(summary) ? "" : Environment.NewLine + summary);
            MessageBox.Show("Minecraft files and ZIP were created successfully.\n\n" + zipPath,
                "Klewi Minecraft Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = "Generation failed: " + ex.Message;
            MessageBox.Show("Klewi could not create the Minecraft output:\n\n" + ex.Message,
                "Minecraft Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            busy = false;
            GenerateMinecraftButton.IsEnabled = true;
        }
    }

    private static string ExtractJsonObject(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("AI did not return a JSON object.");
        return response.Substring(start, end - start + 1);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray();
        var result = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "MinecraftProject" : result;
    }

    private static string GetSafeOutputPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            throw new InvalidDataException("Generated file path must be relative.");
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part == ".." || part == "." || part.Length == 0))
            throw new InvalidDataException("Generated file path contains an unsafe path segment.");
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Generated file path escapes the selected output folder.");
        return fullPath;
    }

    private static void WriteTexturePng(JsonElement root, string outputPath)
    {
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        if ((width != 16 && width != 32) || (height != 16 && height != 32))
            throw new InvalidDataException("Texture dimensions must be 16x16, 16x32, 32x16, or 32x32.");

        var paletteElement = root.GetProperty("palette");
        if (paletteElement.ValueKind != JsonValueKind.Array || paletteElement.GetArrayLength() == 0 || paletteElement.GetArrayLength() > 64)
            throw new InvalidDataException("Texture palette must contain 1 to 64 colors.");
        var palette = new List<Color>();
        foreach (var colorElement in paletteElement.EnumerateArray())
        {
            var hex = (colorElement.GetString() ?? "").Trim().TrimStart('#');
            if (hex.Length == 6)
                palette.Add(Color.FromRgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16)));
            else if (hex.Length == 8)
                palette.Add(Color.FromArgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16), Convert.ToByte(hex.Substring(6, 2), 16)));
            else
                throw new InvalidDataException("Palette colors must be #RRGGBB or #AARRGGBB.");
        }

        var pixels = root.GetProperty("pixels");
        if (pixels.ValueKind != JsonValueKind.Array || pixels.GetArrayLength() != height)
            throw new InvalidDataException("Texture pixel grid has an invalid height.");
        var buffer = new byte[width * height * 4];
        var y = 0;
        foreach (var row in pixels.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != width)
                throw new InvalidDataException("Every texture pixel row must match the selected width.");
            var x = 0;
            foreach (var pixel in row.EnumerateArray())
            {
                var index = pixel.GetInt32();
                if (index < 0 || index >= palette.Count) throw new InvalidDataException("Texture grid references a missing palette color.");
                var color = palette[index];
                var offset = (y * width + x) * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = color.A;
                x++;
            }
            y++;
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private void ChooseIso_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Windows ISO image",
            Filter = "ISO images (*.iso)|*.iso|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        selectedIsoPath = Path.GetFullPath(dialog.FileName);
        IsoPathBox.Text = selectedIsoPath;
        IsoStatusText.Text = "ISO selected. Choose Information + SHA-256 to inspect it, or Mount ISO to browse its files.";
    }

    private async void InspectIso_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(selectedIsoPath) || !File.Exists(selectedIsoPath))
        {
            IsoStatusText.Text = "Спочатку вибери наявний .iso файл.";
            return;
        }
        try
        {
            var info = new FileInfo(selectedIsoPath);
            IsoStatusText.Text = "Обчислюю SHA-256…";
            string hash;
            await using (var stream = new FileStream(selectedIsoPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = await sha.ComputeHashAsync(stream);
                hash = Convert.ToHexString(bytes);
            }
            var version = (IsoWindowsVersionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Windows (not specified)";
            IsoStatusText.Text = $"Target label: {version}\nFile: {info.Name}\nSize: {info.Length:N0} bytes ({info.Length / 1024d / 1024d:N2} MB)\nModified: {info.LastWriteTime}\nSHA-256: {hash}";
        }
        catch (Exception ex)
        {
            IsoStatusText.Text = "Не вдалося перевірити ISO: " + ex.Message;
        }
    }

    private static string QuotePowerShellLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<string> RunPowerShellAsync(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("RemoteSigned");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Не вдалося запустити PowerShell.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Операція з ISO перевищила 45 секунд.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
        return stdout.Trim();
    }

    private async void MountIso_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(selectedIsoPath) || !File.Exists(selectedIsoPath))
        {
            IsoStatusText.Text = "Спочатку вибери наявний .iso файл.";
            return;
        }
        if (mountedIsoPath is not null)
        {
            IsoStatusText.Text = "Спочатку відключи вже підключений образ.";
            return;
        }
        try
        {
            IsoStatusText.Text = "Підключаю ISO як віртуальний DVD…";
            var path = QuotePowerShellLiteral(selectedIsoPath);
            var script = "Mount-DiskImage -ImagePath " + path + " -ErrorAction Stop | Out-Null; " +
                         "$vol = Get-DiskImage -ImagePath " + path + " | Get-Disk | Get-Partition | Get-Volume | Where-Object DriveLetter | Select-Object -First 1; " +
                         "if ($null -eq $vol) { throw 'Образ підключено, але букву диска не знайдено.' }; " +
                         "$vol.DriveLetter";
            var drive = await RunPowerShellAsync(script);
            drive = drive.Trim().TrimEnd(':');
            if (drive.Length != 1 || !char.IsLetter(drive[0]))
                throw new InvalidOperationException("Windows не повернула коректну букву диска: " + drive);
            mountedIsoPath = selectedIsoPath;
            mountedIsoDrive = drive.ToUpperInvariant() + ":";
            IsoStatusText.Text = "ISO підключено. Віртуальний диск: " + mountedIsoDrive + "\\";
        }
        catch (Exception ex)
        {
            IsoStatusText.Text = "Не вдалося підключити ISO: " + ex.Message +
                "\nПереконайся, що працюєш у Windows 10/11 і файл не пошкоджений.";
        }
    }

    private void OpenMountedIso_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(mountedIsoDrive) || !Directory.Exists(mountedIsoDrive + "\\"))
        {
            IsoStatusText.Text = "Спочатку підключи ISO.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = mountedIsoDrive + "\\",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { IsoStatusText.Text = "Не вдалося відкрити Провідник: " + ex.Message; }
    }

    private async void UnmountIso_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(mountedIsoPath))
        {
            IsoStatusText.Text = "Немає підключеного Klewi ISO.";
            return;
        }
        try
        {
            IsoStatusText.Text = "Відключаю віртуальний диск…";
            await RunPowerShellAsync("Dismount-DiskImage -ImagePath " + QuotePowerShellLiteral(mountedIsoPath) + " -ErrorAction Stop | Out-Null");
            mountedIsoPath = null;
            mountedIsoDrive = null;
            IsoStatusText.Text = "ISO безпечно відключено.";
        }
        catch (Exception ex) { IsoStatusText.Text = "Не вдалося відключити ISO: " + ex.Message; }
    }

    private void SettingsNav_Click(object s, RoutedEventArgs e)
    {
        ApiKeyBox.Password = apiKey;
        ProviderCombo.SelectedIndex = provider switch { "OpenAI" => 1, "OpenRouter" => 2, _ => 0 };
        ModelBox.Text = model;
        SettingsInstructionsBox.Text = customInstructions;
        SettingsTemperatureSlider.Value = chatTemperature;
        SettingsMaxTokensBox.Text = chatMaxTokens.ToString();
        SettingsMinecraftVersionBox.Text = MinecraftVersionBox.Text;
        SettingsMinecraftLoaderCombo.SelectedIndex = MinecraftLoaderCombo.SelectedIndex;
        SettingsMinecraftTaskCombo.SelectedIndex = MinecraftTaskCombo.SelectedIndex;
        SettingsMinecraftOutputBox.Text = MinecraftOutputPathBox.Text;
        Navigate("settings", "Settings", "Instructions, API keys, Minecraft Studio and chat preferences.");
    }

    private void InstructionPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsInstructionsBox is null || InstructionPresetCombo.SelectedItem is not ComboBoxItem item) return;
        var preset = item.Content?.ToString();
        if (preset == "Custom instructions") return;
        SettingsInstructionsBox.Text = preset switch
        {
            "Explain step by step" => "Explain technical tasks in clear, numbered steps. Define unfamiliar terms and show complete examples when useful.",
            "Short answers" => "Answer briefly and directly. Use short numbered steps for troubleshooting. Avoid unnecessary background.",
            "Minecraft mod developer" => "Act as an experienced Minecraft Java mod developer. Respect the exact Minecraft version and loader. Never invent APIs or claim a mod compiles unless a build was actually run.",
            _ => "You are Klewi, a helpful coding assistant. Be accurate, practical, and honest. Explain errors clearly and never claim to have changed files unless an available tool actually did so."
        };
    }
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
            if (doc.RootElement.TryGetProperty("instructions", out var instructions)) customInstructions = instructions.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("temperature", out var temperature)) chatTemperature = Math.Clamp(temperature.GetDouble(), 0, 1);
            if (doc.RootElement.TryGetProperty("maxTokens", out var maxTokens)) chatMaxTokens = Math.Clamp(maxTokens.GetInt32(), 256, 8192);
            if (doc.RootElement.TryGetProperty("minecraftVersion", out var mv)) minecraftDefaultVersion = mv.GetString() ?? minecraftDefaultVersion;
            if (doc.RootElement.TryGetProperty("minecraftLoader", out var ml)) minecraftDefaultLoader = ml.GetString() ?? minecraftDefaultLoader;
            if (doc.RootElement.TryGetProperty("minecraftTask", out var mt)) minecraftDefaultTask = mt.GetString() ?? minecraftDefaultTask;
            if (doc.RootElement.TryGetProperty("minecraftOutputFolder", out var mo)) minecraftOutputFolder = mo.GetString() ?? "";
            MinecraftVersionBox.Text = minecraftDefaultVersion;
            MinecraftLoaderCombo.SelectedIndex = LoaderIndex(minecraftDefaultLoader);
            MinecraftTaskCombo.SelectedIndex = TaskIndex(minecraftDefaultTask);
            if (!string.IsNullOrWhiteSpace(minecraftOutputFolder)) MinecraftOutputPathBox.Text = minecraftOutputFolder;
        } catch { apiKey = ""; }
    }
    private static int LoaderIndex(string value) => value switch { "Forge" => 1, "NeoForge" => 2, "Not applicable (resource pack)" => 3, _ => 0 };
    private static int TaskIndex(string value) => value switch { "Resource pack" => 1, "Texture design (pixel art)" => 2, _ => 0 };

    private void SaveSettings_Click(object s, RoutedEventArgs e)
    {
        try {
            var enteredKey = ApiKeyBox.Password.Trim();
            if (enteredKey.Length > 0) apiKey = enteredKey;
            provider = (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Gemini";
            model = ModelBox.Text.Trim();
            if (model.Length == 0) { SettingsStatus.Text = "Enter a model ID."; return; }
            if (apiKey.Length == 0) { SettingsStatus.Text = "Enter an API key first."; return; }
            customInstructions = SettingsInstructionsBox.Text.Trim();
            chatTemperature = Math.Clamp(SettingsTemperatureSlider.Value, 0, 1);
            if (!int.TryParse(SettingsMaxTokensBox.Text.Trim(), out chatMaxTokens) || chatMaxTokens < 256 || chatMaxTokens > 8192)
            { SettingsStatus.Text = "Maximum response tokens must be between 256 and 8192."; return; }
            minecraftDefaultVersion = SettingsMinecraftVersionBox.Text.Trim();
            if (minecraftDefaultVersion.Length == 0) { SettingsStatus.Text = "Enter a default Minecraft version."; return; }
            minecraftDefaultLoader = (SettingsMinecraftLoaderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Fabric";
            minecraftDefaultTask = (SettingsMinecraftTaskCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Minecraft mod (Java)";
            minecraftOutputFolder = SettingsMinecraftOutputBox.Text.Trim();
            MinecraftVersionBox.Text = minecraftDefaultVersion;
            MinecraftLoaderCombo.SelectedIndex = LoaderIndex(minecraftDefaultLoader);
            MinecraftTaskCombo.SelectedIndex = TaskIndex(minecraftDefaultTask);
            if (!string.IsNullOrWhiteSpace(minecraftOutputFolder)) MinecraftOutputPathBox.Text = minecraftOutputFolder;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var enc = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser));
            var data = new { key = enc, model, provider, instructions = customInstructions, temperature = chatTemperature, maxTokens = chatMaxTokens, minecraftVersion = minecraftDefaultVersion, minecraftLoader = minecraftDefaultLoader, minecraftTask = minecraftDefaultTask, minecraftOutputFolder };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
            SettingsStatus.Text = "All settings saved securely. API key is encrypted for this Windows account.";
            UpdateStatus();
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
    private string BuildSystemInstruction()
    {
        const string baseline = "You are Klewi, a multilingual software engineering assistant. Help with programming languages and ecosystems broadly, including C, C++, C#, F#, Java, Kotlin, Scala, Python, JavaScript, TypeScript, Go, Rust, Swift, Objective-C, PHP, Ruby, Perl, Lua, Dart, R, Julia, MATLAB, SQL, HTML, CSS, XML, YAML, JSON, Bash, PowerShell, Windows batch, assembly (including FASM), Solidity, and domain-specific languages. If a language or framework is unfamiliar, say so and reason carefully rather than inventing APIs. Match project conventions, identify language and runtime versions, provide complete code, explain dependencies and exact build/run commands, and distinguish tested facts from assumptions. Be accurate, practical, and honest. You can only inspect project file contents explicitly attached in chat or opened in the project editor. You may propose complete file edits, but the desktop app writes them only after the user reviews and approves them. Never claim a file was changed or a project was built unless the app confirms that action.";
        return string.IsNullOrWhiteSpace(customInstructions) ? baseline : baseline + Environment.NewLine + Environment.NewLine + "Additional user instructions:" + Environment.NewLine + customInstructions;
    }

    private async Task<string> AskProvider(string prompt)
    {
        if (provider == "Gemini")
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var payload = new { systemInstruction = new { parts = new[] { new { text = BuildSystemInstruction() } } }, contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = chatTemperature, maxOutputTokens = chatMaxTokens } };
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
            messages = new[] { new { role = "system", content = BuildSystemInstruction() }, new { role = "user", content = prompt } },
            temperature = chatTemperature,
            max_tokens = chatMaxTokens
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
        var prompt = ChatInput.Text.Trim();
        if (prompt.Length == 0 || busy) return;
        if (apiKey.Length == 0) { AddMessage("Klewi", "Open Settings and configure an AI provider first."); return; }

        ChatInput.Clear();
        AddMessage("You", prompt);
        var pending = AddMessage("Klewi", "Thinking…");
        busy = true;
        var attachedContext = pendingFileContext;
        pendingFileContext = "";
        try
        {
            var language = (ChatLanguageCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto-detect";
            var languageInstruction = language == "Auto-detect" ? "Programming language mode: auto-detect from the request and attached code." : "Programming language mode requested by user: " + language + ". Prioritize it, but use other languages when required.";
            var fullPrompt = languageInstruction + "\nFor coding tasks, provide complete usable code and appropriate build/run/test commands. Never claim execution or tests unless performed.\n\n" + prompt
                + (attachedContext.Length == 0 ? "" : "\n\nUser-selected project file:\n" + attachedContext)
                + (projectPath is null ? "" : $"\nWorkspace folder: {projectPath}. Only the attached file content is available; do not claim to have read other files.");
            pending.Text = await AskProvider(fullPrompt);
        }
        catch (Exception ex) { pending.Text = "Request failed: " + ex.Message; }
        finally { busy = false; ChatScroll.ScrollToEnd(); }
    }

    private void OpenProject_Click(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose project folder" };
        if (dialog.ShowDialog() != true) return;
        projectPath = Path.GetFullPath(dialog.FolderName);
        ProjectPathText.Text = projectPath;
        selectedFilePath = null;
        loadedFileContents = "";
        FilePreview.Clear();
        FilePreviewStatus.Text = "Select a text file to preview or edit it.";
        RefreshProjectTree();
    }

    private void RefreshProjectTree()
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return;
        ProjectTree.Items.Clear();
        var root = new TreeViewItem { Header = Path.GetFileName(projectPath), Tag = projectPath, IsExpanded = true };
        ProjectTree.Items.Add(root);
        try { FillTree(root, projectPath, 0); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Klewi"); }
    }

    private string EnsureInsideProject(string path)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) throw new InvalidOperationException("Open a project folder first.");
        var root = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Klewi can only edit files inside the selected project folder.");
        return full;
    }

    private static string CreateBackup(string filePath, string rootPath)
    {
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KlewiDesktop", "Backups");
        Directory.CreateDirectory(backupRoot);
        var relative = Path.GetRelativePath(rootPath, filePath).Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        var backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + relative + ".bak");
        File.Copy(filePath, backup, false);
        return backup;
    }

    private void CreateProjectFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            MessageBox.Show("Open a project folder first.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Create a new project file",
            InitialDirectory = projectPath,
            Filter = "All files (*.*)|*.*",
            FileName = "NewFile.txt",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = EnsureInsideProject(dialog.FileName);
            if (File.Exists(path))
            {
                MessageBox.Show("That file already exists. Choose a new name.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "", new UTF8Encoding(false));
            RefreshProjectTree();
            selectedFilePath = path;
            loadedFileContents = "";
            FilePreview.Text = "";
            FilePreviewStatus.Text = "New file created: " + Path.GetRelativePath(projectPath, path) + ". Edit it and press Save file.";
        }
        catch (Exception ex) { MessageBox.Show("Could not create file: " + ex.Message, "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(selectedFilePath) || !File.Exists(selectedFilePath) || projectPath is null)
        {
            MessageBox.Show("Select an existing project text file first. Use New file to create one.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var path = EnsureInsideProject(selectedFilePath);
            if (IsSensitiveProjectFile(path)) throw new InvalidOperationException("Saving files with sensitive names is blocked.");
            var diskContents = File.ReadAllText(path);
            if (!string.Equals(diskContents, loadedFileContents, StringComparison.Ordinal))
            {
                MessageBox.Show("This file changed outside Klewi after it was opened. Reload it before saving to avoid overwriting newer work.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.Equals(FilePreview.Text, diskContents, StringComparison.Ordinal))
            {
                var backup = CreateBackup(path, projectPath);
                File.WriteAllText(path, FilePreview.Text, new UTF8Encoding(false));
                loadedFileContents = FilePreview.Text;
                FilePreviewStatus.Text = "Saved • backup: " + backup;
            }
            else FilePreviewStatus.Text = "No changes to save.";
            RefreshProjectTree();
        }
        catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message, "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private string? PromptForText(string title, string prompt, string initialText = "")
    {
        var dialog = new Window
        {
            Title = title, Width = 650, Height = 300, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("Panel"), Foreground = Brushes.White,
            ResizeMode = ResizeMode.CanResize
        };
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(label, 0); layout.Children.Add(label);
        var input = new TextBox { Text = initialText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, Background = new SolidColorBrush(Color.FromRgb(11, 16, 24)), Foreground = Brushes.White, Padding = new Thickness(9) };
        Grid.SetRow(input, 1); layout.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var ok = new Button { Content = "Continue", MinWidth = 100, Background = (Brush)FindResource("Accent") };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
        dialog.Content = layout;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private bool ShowFileChangePreview(string title, string summary, string before, string after)
    {
        var dialog = new Window
        {
            Title = title, Width = 1100, Height = 720, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("Panel"), Foreground = Brushes.White,
            ResizeMode = ResizeMode.CanResize
        };
        var layout = new Grid { Margin = new Thickness(14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var description = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(description, 0); Grid.SetColumnSpan(description, 2); layout.Children.Add(description);
        var oldBox = new TextBox { Text = before, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(11, 16, 24)), Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 6, 0) };
        var newBox = new TextBox { Text = after, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(11, 16, 24)), Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetRow(oldBox, 1); Grid.SetColumn(oldBox, 0); layout.Children.Add(oldBox);
        Grid.SetRow(newBox, 1); Grid.SetColumn(newBox, 1); layout.Children.Add(newBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Discard", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var apply = new Button { Content = "Apply changes", MinWidth = 120, Background = (Brush)FindResource("Accent") };
        apply.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        Grid.SetRow(buttons, 2); Grid.SetColumnSpan(buttons, 2); layout.Children.Add(buttons);
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }

    private async void AskAIEdit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("Configure an AI provider in Settings first.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(selectedFilePath) || projectPath is null || !File.Exists(selectedFilePath))
        {
            MessageBox.Show("Select a project text file first.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (busy) return;
        try
        {
            var path = EnsureInsideProject(selectedFilePath);
            if (IsSensitiveProjectFile(path)) throw new InvalidOperationException("AI access to files with sensitive names is blocked.");
            var before = FilePreview.Text;
            var diskBefore = File.ReadAllText(path);
            if (Encoding.UTF8.GetByteCount(before) > 500 * 1024) throw new InvalidOperationException("AI editing is limited to files up to 500 KB.");
            var task = PromptForText("Ask Klewi to edit this file", "Describe the exact changes you want. Klewi will show a before/after preview and will not write until you apply it.");
            if (string.IsNullOrWhiteSpace(task)) return;
            busy = true;
            AskAIEditButton.IsEnabled = false;
            FilePreviewStatus.Text = "Klewi is preparing a proposed edit…";
            var detectedLanguage = DetectProgrammingLanguage(path);
            var prompt = "You are editing a " + detectedLanguage + " project file. Respect its syntax, compiler/runtime version, project conventions, and dependencies. Edit the supplied file according to the request. Return exactly one JSON object with string keys summary and content. content must be the COMPLETE replacement file, not a diff, with no Markdown fences. Do not include unrelated changes. Do not claim you wrote the file.\n\nFILE PATH: "
                + Path.GetRelativePath(projectPath, path) + "\n\nUSER REQUEST:\n" + task + "\n\nCURRENT FILE CONTENT:\n" + before;
            var response = await AskProvider(prompt);
            using var doc = JsonDocument.Parse(ExtractJsonObject(response));
            var summary = doc.RootElement.GetProperty("summary").GetString() ?? "AI proposed file changes.";
            var after = doc.RootElement.GetProperty("content").GetString() ?? throw new InvalidDataException("AI did not return file content.");
            if (Encoding.UTF8.GetByteCount(after) > 1024 * 1024) throw new InvalidDataException("AI edit exceeds the 1 MB file limit.");
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                FilePreviewStatus.Text = "AI did not propose any changes.";
                return;
            }
            if (!ShowFileChangePreview("Review AI file changes", summary, before, after))
            {
                FilePreviewStatus.Text = "AI changes discarded. Your file was not modified.";
                return;
            }
            var diskNow = File.ReadAllText(path);
            if (!string.Equals(diskNow, diskBefore, StringComparison.Ordinal))
                throw new IOException("The file changed on disk while Klewi was preparing the edit. Nothing was applied; reload the file and try again.");
            var backup = CreateBackup(path, projectPath);
            File.WriteAllText(path, after, new UTF8Encoding(false));
            FilePreview.Text = after;
            loadedFileContents = after;
            FilePreviewStatus.Text = "AI changes applied • backup: " + backup;
            RefreshProjectTree();
        }
        catch (Exception ex)
        {
            FilePreviewStatus.Text = "AI edit failed: " + ex.Message;
            MessageBox.Show("Klewi could not apply the edit:\n\n" + ex.Message, "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { busy = false; AskAIEditButton.IsEnabled = true; }
    }

    private void AskAIEditButtonBusyState(bool isBusy)
    {
        foreach (var child in ((DockPanel)ProjectPage.Children[0]).Children)
            if (child is StackPanel panel)
                foreach (var item in panel.Children)
                    if (item is Button button && button.Content?.ToString() == "AI edit") button.IsEnabled = !isBusy;
    }
    private static void FillTree(TreeViewItem node, string path, int depth)
    {
        if (depth >= 3) return;
        foreach (var dir in Directory.EnumerateDirectories(path).Where(x => !Path.GetFileName(x).StartsWith('.') && !new[] { "bin", "obj", "node_modules", ".git", ".vs" }.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)).Take(60)) {
            var child = new TreeViewItem { Header = "📁 " + Path.GetFileName(dir) }; node.Items.Add(child);
            try { FillTree(child, dir, depth + 1); } catch { }
        }
        foreach (var file in Directory.EnumerateFiles(path).Take(100)) node.Items.Add(new TreeViewItem { Header = "📄 " + Path.GetFileName(file), Tag = file });
    }

    private static readonly HashSet<string> PreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".xaml", ".json", ".jsonc", ".xml", ".md", ".txt", ".py", ".pyw", ".pyi",
        ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".cpp", ".c", ".h", ".hpp", ".hh", ".hxx", ".cc", ".cxx", ".asm", ".inc", ".s", ".S",
        ".ps1", ".psm1", ".psd1", ".yml", ".yaml", ".toml", ".ini", ".java", ".kt", ".kts", ".scala",
        ".gradle", ".properties", ".sql", ".sh", ".bash", ".zsh", ".fish", ".bat", ".cmd", ".sln",
        ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".go", ".rs", ".php", ".rb", ".r",
        ".jl", ".swift", ".m", ".mm", ".dart", ".lua", ".pl", ".pm", ".ex", ".exs", ".hs", ".lhs",
        ".clj", ".cljs", ".cljc", ".edn", ".sol", ".move", ".tf", ".hcl", ".mk", ".cmake", ".proto",
        ".graphql", ".gql", ".vue", ".svelte", ".astro", ".ipynb"
    };

    private static string DetectProgrammingLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "C#", ".fs" or ".fsproj" => "F#", ".vb" => "Visual Basic",
            ".c" or ".h" => "C", ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx" => "C++",
            ".py" or ".pyw" or ".pyi" or ".ipynb" => "Python",
            ".js" or ".mjs" or ".cjs" or ".jsx" => "JavaScript", ".ts" or ".tsx" => "TypeScript",
            ".java" => "Java", ".kt" or ".kts" => "Kotlin", ".scala" => "Scala", ".go" => "Go",
            ".rs" => "Rust", ".swift" => "Swift", ".php" => "PHP", ".rb" => "Ruby", ".pl" or ".pm" => "Perl",
            ".lua" => "Lua", ".dart" => "Dart", ".r" => "R", ".jl" => "Julia", ".sql" => "SQL",
            ".html" or ".htm" => "HTML", ".css" or ".scss" or ".sass" or ".less" => "CSS / stylesheet",
            ".sh" or ".bash" or ".zsh" or ".fish" => "Shell script", ".ps1" or ".psm1" or ".psd1" => "PowerShell",
            ".bat" or ".cmd" => "Windows batch", ".asm" or ".inc" or ".s" => "Assembly (verify assembler syntax)",
            ".sol" => "Solidity", ".ex" or ".exs" => "Elixir", ".hs" or ".lhs" => "Haskell",
            ".clj" or ".cljs" or ".cljc" or ".edn" => "Clojure", ".tf" or ".hcl" => "Terraform / HCL",
            ".vue" => "Vue component", ".svelte" => "Svelte component", ".astro" => "Astro component",
            ".xml" or ".xaml" => "XML", ".json" or ".jsonc" => "JSON", ".yml" or ".yaml" => "YAML",
            ".toml" => "TOML", ".md" => "Markdown", _ => "text/configuration or unrecognized language; infer from contents"
        };
    }

    private static bool IsSensitiveProjectFile(string path)
    {
        var lower = Path.GetFileName(path).ToLowerInvariant();
        return lower == ".env" || lower.StartsWith(".env.") ||
            lower.Contains("secret") || lower.Contains("credential") ||
            lower.Contains("password") || lower.Contains("token") ||
            lower.Contains("private_key") || lower == "id_rsa" || lower == "id_ed25519" ||
            lower.EndsWith(".pem") || lower.EndsWith(".pfx") || lower.EndsWith(".p12");
    }

    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        selectedFilePath = null;
        FilePreview.Clear();
        if (ProjectTree.SelectedItem is not TreeViewItem item || item.Tag is not string path || !File.Exists(path))
        {
            FilePreviewStatus.Text = "Select a text file to preview it.";
            return;
        }
        selectedFilePath = path;
        if (IsSensitiveProjectFile(path))
        {
            FilePreviewStatus.Text = "Preview blocked: this filename may contain credentials or secrets.";
            selectedFilePath = null;
            return;
        }
        if (!PreviewExtensions.Contains(Path.GetExtension(path)))
        {
            FilePreviewStatus.Text = "Preview supports source code and text files only.";
            selectedFilePath = null;
            return;
        }
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 100 * 1024)
            {
                FilePreviewStatus.Text = "File is larger than the 100 KB preview limit.";
                selectedFilePath = null;
                return;
            }
            FilePreview.Text = File.ReadAllText(path);
            loadedFileContents = FilePreview.Text;
            FilePreviewStatus.Text = $"{Path.GetFileName(path)} • {info.Length:N0} bytes • editable";
        }
        catch (Exception ex)
        {
            FilePreviewStatus.Text = "Could not read file: " + ex.Message;
            selectedFilePath = null;
        }
    }

    private void UseFileInChat_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(selectedFilePath) || projectPath is null)
        {
            MessageBox.Show("Select a supported text file first.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            if (IsSensitiveProjectFile(selectedFilePath))
            {
                MessageBox.Show("This file may contain secrets and cannot be attached.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var info = new FileInfo(selectedFilePath);
            if (info.Length > 100 * 1024)
            {
                MessageBox.Show("Files larger than 100 KB cannot be attached yet.", "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var relative = Path.GetRelativePath(projectPath, selectedFilePath);
            var contents = FilePreview.Text;
            pendingFileContext = $"File: {relative}{Environment.NewLine}{contents}";
            Navigate("chat", "AI Chat", "Ask Klewi about the selected project file.");
            AddMessage("Klewi", $"Attached {relative} as context for your next message. It will be sent to your selected AI provider.");
            ChatInput.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not attach file: " + ex.Message, "Klewi Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BuildMinecraftJar_Click(object sender, RoutedEventArgs e)
    {
        var buildDirectory = lastMinecraftProjectDirectory;
        if (string.IsNullOrWhiteSpace(buildDirectory) || !Directory.Exists(buildDirectory))
        {
            var picker = new OpenFolderDialog { Title = "Choose the generated Minecraft mod project folder" };
            if (picker.ShowDialog(this) != true) return;
            buildDirectory = picker.FolderName;
        }

        var hasBuildFile = File.Exists(Path.Combine(buildDirectory, "build.gradle")) ||
                           File.Exists(Path.Combine(buildDirectory, "build.gradle.kts"));
        if (!hasBuildFile)
        {
            MinecraftStatusText.Text = "This folder has no Gradle build.gradle or build.gradle.kts file.";
            return;
        }
        var wrapper = Path.Combine(buildDirectory, "gradlew.bat");
        var useWrapper = File.Exists(wrapper);
        var confirmation = MessageBox.Show(
            "Klewi is about to run the Gradle 'build' task in:\n" + buildDirectory +
            "\n\nGradle build scripts can execute code on your computer. Review build.gradle, settings.gradle and wrapper files first. Continue?",
            "Confirm Minecraft mod build", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        if (busy) return;

        busy = true;
        BuildMinecraftButton.IsEnabled = false;
        MinecraftBuildOutput.Text = "Starting Gradle build…";
        MinecraftStatusText.Text = "Building mod. This can take several minutes and may download dependencies.";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = buildDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(useWrapper ? "\"" + wrapper + "\" build" : "gradle build");
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the Gradle process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new TimeoutException("Gradle build exceeded the 5-minute limit.");
            }
            var output = await stdoutTask;
            var errors = await stderrTask;
            var combined = (output + Environment.NewLine + errors).Trim();
            MinecraftBuildOutput.Text = combined.Length > 24000 ? combined[^24000..] : combined;
            if (process.ExitCode != 0)
            {
                MinecraftStatusText.Text = "Build failed (exit code " + process.ExitCode + "). Read the build output below.";
                return;
            }
            var libs = Path.Combine(buildDirectory, "build", "libs");
            var jars = Directory.Exists(libs)
                ? Directory.EnumerateFiles(libs, "*.jar").Where(p =>
                    !Path.GetFileName(p).Contains("sources", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(p).Contains("javadoc", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(p).Contains("dev", StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();
            MinecraftStatusText.Text = jars.Count == 0
                ? "Gradle build succeeded, but no distributable .jar was found in build/libs."
                : "Build succeeded! .jar file(s):\n" + string.Join("\n", jars);
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = "Build error: " + ex.Message;
            MinecraftBuildOutput.Text += Environment.NewLine + ex;
        }
        finally { busy = false; BuildMinecraftButton.IsEnabled = true; }
    }

    private async void RunTerminal_Click(object s, RoutedEventArgs e) => await RunCommand();
    private async void TerminalInput_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await RunCommand(); } }
    private async Task RunCommand()
    {
        var cmd = TerminalInput.Text.Trim();
        if (cmd.Length == 0) return;
        TerminalInput.Clear();
        TerminalOutput.Text += $"\n> {cmd}\n";

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pwd", "dir", "ls", "dotnet", "python", "python3", "node", "npm", "git" };
        var words = SplitArgs(cmd);
        var exe = words[0].ToLowerInvariant();
        if (!allowed.Contains(exe)) { TerminalOutput.Text += "Blocked: command is not on the allowlist.\n"; return; }

        bool permitted = exe switch
        {
            "pwd" or "dir" or "ls" => words.Count == 1,
            "git" => cmd.Equals("git status", StringComparison.OrdinalIgnoreCase),
            "dotnet" => cmd.Equals("dotnet --info", StringComparison.OrdinalIgnoreCase) || cmd.Equals("dotnet --version", StringComparison.OrdinalIgnoreCase) || cmd.Equals("dotnet build", StringComparison.OrdinalIgnoreCase),
            "python" or "python3" or "node" => words.Count == 2 && (words[1] == "--version" || words[1] == "-V" || words[1] == "-v"),
            "npm" => cmd.Equals("npm --version", StringComparison.OrdinalIgnoreCase) || cmd.Equals("npm -v", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!permitted) { TerminalOutput.Text += "Blocked: only safe version checks, git status, and dotnet build are enabled.\n"; return; }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe is "pwd" or "dir" or "ls" ? "powershell.exe" : exe,
                WorkingDirectory = projectPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (exe is "pwd" or "dir" or "ls")
            {
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(exe == "pwd" ? "Get-Location" : "Get-ChildItem -Name");
            }
            else
            {
                foreach (var arg in words.Skip(1)) psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            TerminalOutput.Text += await stdout + await stderr + $"\n[exit code {process.ExitCode}]\n";
        }
        catch (OperationCanceledException) { TerminalOutput.Text += "Command timed out after 20 seconds.\n"; }
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
