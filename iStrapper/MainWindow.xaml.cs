using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace iStrapper
{
    public class Mod
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
    }

    public partial class MainWindow : Window
    {
        // Win32 API
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr addr, uint size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr process, IntPtr addr, byte[] buffer, uint size, out uint written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attr, uint stack, IntPtr start, IntPtr param, uint flags, IntPtr threadId);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetProcAddress(IntPtr module, string proc);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        private const string GITHUB_API = "https://api.github.com/repos/";
        private const string ROBLOX_VERSION_URL = "https://setup.rbxcdn.com/version";
        private const string ROBLOX_DOWNLOAD_URL = "https://setup.rbxcdn.com/{version}/RobloxPlayerLauncher.exe";

        private static readonly string RobloxInstallPath = @"C:\Roblox";
        private static readonly string ModsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
        private static readonly string FontsFolder = Path.Combine(ModsFolder, "Fonts");
        private static readonly string ScriptsFolder = Path.Combine(ModsFolder, "Scripts");
        private static readonly string TexturesFolder = Path.Combine(ModsFolder, "Textures");
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "istrapper_config.json");

        private HttpClient _httpClient;
        private List<Mod> _mods = new List<Mod>();
        private int _fpsLimit = 60;
        private bool _unlockFps = false;

        public MainWindow()
        {
            InitializeComponent();
            
            SetProcessDPIAware();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("iStrapper/1.0");
            
            InitializeFolders();
            LoadConfig();
            LoadMods();
            CheckRobloxInstallation();
            CheckForUpdates();
            
            FpsSlider.ValueChanged += FpsSlider_ValueChanged;
            FontSizeSlider.ValueChanged += FontSizeSlider_ValueChanged;
        }

        private void InitializeFolders()
        {
            Directory.CreateDirectory(ModsFolder);
            Directory.CreateDirectory(FontsFolder);
            Directory.CreateDirectory(ScriptsFolder);
            Directory.CreateDirectory(TexturesFolder);
            Log("iStrapper initialized");
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogBox.ScrollToEnd();
            });
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        
                        if (root.TryGetProperty("fpsLimit", out JsonElement fps))
                            _fpsLimit = fps.GetInt32();
                        
                        if (root.TryGetProperty("unlockFps", out JsonElement unlock))
                            _unlockFps = unlock.GetBoolean();
                        
                        if (root.TryGetProperty("installPath", out JsonElement path))
                            InstallPathBox.Text = path.GetString();
                    }
                    
                    FpsSlider.Value = _fpsLimit;
                    UnlockFpsCheck.IsChecked = _unlockFps;
                    UpdateFpsDisplay();
                    Log("Config loaded");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load config: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new
                {
                    fpsLimit = _fpsLimit,
                    unlockFps = _unlockFps,
                    installPath = InstallPathBox.Text,
                    graphicsQuality = GraphicsQualityBox.SelectedIndex,
                    font = FontBox.SelectedIndex,
                    fontSize = FontSizeSlider.Value,
                    enableiOS = EnableiOSCheck.IsChecked,
                    iOSDevice = iOSDeviceBox.SelectedIndex,
                    iOSVersion = iOSVersionBox.Text
                };
                
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                Log("Config saved");
            }
            catch (Exception ex)
            {
                Log($"Failed to save config: {ex.Message}");
            }
        }

        private void LoadMods()
        {
            _mods.Clear();
            
            // Load fonts
            foreach (string fontFile in Directory.GetFiles(FontsFolder, "*.ttf"))
            {
                _mods.Add(new Mod
                {
                    Name = Path.GetFileNameWithoutExtension(fontFile),
                    Description = "Custom Font",
                    Enabled = true,
                    Path = fontFile,
                    Type = "Font"
                });
            }
            
            // Load scripts
            foreach (string scriptFile in Directory.GetFiles(ScriptsFolder, "*.lua"))
            {
                _mods.Add(new Mod
                {
                    Name = Path.GetFileNameWithoutExtension(scriptFile),
                    Description = "Lua Script",
                    Enabled = true,
                    Path = scriptFile,
                    Type = "Script"
                });
            }
            
            // Load textures
            foreach (string textureFile in Directory.GetFiles(TexturesFolder, "*.png"))
            {
                _mods.Add(new Mod
                {
                    Name = Path.GetFileNameWithoutExtension(textureFile),
                    Description = "Texture",
                    Enabled = true,
                    Path = textureFile,
                    Type = "Texture"
                });
            }
            
            ModListBox.ItemsSource = _mods;
            Log($"Loaded {_mods.Count} mods");
        }

        private void RefreshMods_Click(object sender, RoutedEventArgs e)
        {
            LoadMods();
        }

        private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", ModsFolder);
        }

        private void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Mod Files (*.ttf;*.lua;*.png;*.zip)|*.ttf;*.lua;*.png;*.zip|All Files (*.*)|*.*",
                Title = "Select Mod to Install"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string file = dialog.FileName;
                    string extension = Path.GetExtension(file).ToLower();
                    string destFolder;
                    
                    switch (extension)
                    {
                        case ".ttf":
                            destFolder = FontsFolder;
                            break;
                        case ".lua":
                            destFolder = ScriptsFolder;
                            break;
                        case ".png":
                            destFolder = TexturesFolder;
                            break;
                        case ".zip":
                            string extractPath = Path.Combine(ModsFolder, Path.GetFileNameWithoutExtension(file));
                            ZipFile.ExtractToDirectory(file, extractPath);
                            Log($"Extracted mod to {extractPath}");
                            LoadMods();
                            return;
                        default:
                            destFolder = ModsFolder;
                            break;
                    }
                    
                    string destPath = Path.Combine(destFolder, Path.GetFileName(file));
                    File.Copy(file, destPath, true);
                    Log($"Installed mod: {Path.GetFileName(file)}");
                    LoadMods();
                }
                catch (Exception ex)
                {
                    Log($"Failed to install mod: {ex.Message}");
                    MessageBox.Show($"Failed to install mod: {ex.Message}");
                }
            }
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (ModListBox.SelectedItem is Mod selected)
            {
                try
                {
                    if (File.Exists(selected.Path))
                    {
                        File.Delete(selected.Path);
                        Log($"Removed mod: {selected.Name}");
                        LoadMods();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to remove mod: {ex.Message}");
                }
            }
        }

        private void ApplyMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("Applying mods...");
                StatusText.Text = "Applying mods...";
                StatusText.Foreground = Brushes.Orange;
                
                int appliedCount = 0;
                
                foreach (Mod mod in _mods.Where(m => m.Enabled))
                {
                    switch (mod.Type)
                    {
                        case "Font":
                            ApplyFontMod(mod);
                            break;
                        case "Script":
                            ApplyScriptMod(mod);
                            break;
                        case "Texture":
                            ApplyTextureMod(mod);
                            break;
                    }
                    appliedCount++;
                }
                
                Log($"Applied {appliedCount} mods");
                StatusText.Text = $"{appliedCount} mods applied";
                StatusText.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                Log($"Failed to apply mods: {ex.Message}");
                StatusText.Text = "Failed to apply mods";
                StatusText.Foreground = Brushes.Red;
            }
        }

        private void ApplyFontMod(Mod mod)
        {
            string robloxFonts = Path.Combine(RobloxInstallPath, "content", "fonts");
            Directory.CreateDirectory(robloxFonts);
            
            string destPath = Path.Combine(robloxFonts, Path.GetFileName(mod.Path));
            File.Copy(mod.Path, destPath, true);
            Log($"Applied font: {mod.Name}");
        }

        private void ApplyScriptMod(Mod mod)
        {
            string robloxScripts = Path.Combine(RobloxInstallPath, "content", "scripts");
            Directory.CreateDirectory(robloxScripts);
            
            string destPath = Path.Combine(robloxScripts, Path.GetFileName(mod.Path));
            File.Copy(mod.Path, destPath, true);
            Log($"Applied script: {mod.Name}");
        }

        private void ApplyTextureMod(Mod mod)
        {
            string robloxTextures = Path.Combine(RobloxInstallPath, "content", "textures");
            Directory.CreateDirectory(robloxTextures);
            
            string destPath = Path.Combine(robloxTextures, Path.GetFileName(mod.Path));
            File.Copy(mod.Path, destPath, true);
            Log($"Applied texture: {mod.Name}");
        }

        private void ApplyFps_Click(object sender, RoutedEventArgs e)
        {
            _fpsLimit = (int)FpsSlider.Value;
            _unlockFps = UnlockFpsCheck.IsChecked == true;
            
            SaveConfig();
            ApplyFpsSettings();
        }

        private void ApplyFpsSettings()
        {
            try
            {
                string fpsConfig = Path.Combine(RobloxInstallPath, "ClientSettings", "ClientAppSettings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(fpsConfig));
                
                var settings = new Dictionary<string, object>
                {
                    ["DFIntTaskSchedulerTargetFps"] = _unlockFps ? 9999 : _fpsLimit,
                    ["FFlagDebugGraphicsDisableQuads"] = false,
                    ["FFlagFastGPULightCulling3"] = true,
                    ["FFlagDebugGraphicsPreferD3D11"] = true
                };
                
                if (GraphicsQualityBox.SelectedIndex == 0) // Low
                {
                    settings["DFIntMaxFrameRate"] = 30;
                    settings["FFlagDisablePostFx"] = true;
                }
                else if (GraphicsQualityBox.SelectedIndex == 3) // Ultra
                {
                    settings["DFIntMaxFrameRate"] = _unlockFps ? 9999 : _fpsLimit;
                    settings["FFlagDisablePostFx"] = false;
                }
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fpsConfig, json);
                
                Log($"Applied FPS settings: {(_unlockFps ? "Unlocked" : _fpsLimit.ToString() + " FPS")}");
            }
            catch (Exception ex)
            {
                Log($"Failed to apply FPS settings: {ex.Message}");
            }
        }

        private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateFpsDisplay();
        }

        private void UpdateFpsDisplay()
        {
            int fps = (int)FpsSlider.Value;
            FpsValueText.Text = UnlockFpsCheck.IsChecked == true ? "🔓 Unlocked" : $"{fps} FPS";
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            FontSizeText.Text = $"{FontSizeSlider.Value:0}px";
        }

        private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontBox.SelectedIndex == 1)
            {
                FontFamily customFont = new FontFamily("pack://application:,,,/Fonts/#iStrapperFont");
                this.FontFamily = customFont;
            }
            else if (FontBox.SelectedIndex == 2)
            {
                this.FontFamily = new FontFamily("Consolas");
            }
            else
            {
                this.FontFamily = new FontFamily("Segoe UI");
            }
        }

        private void CheckRobloxInstallation()
        {
            if (Directory.Exists(RobloxInstallPath))
            {
                string versionFile = Path.Combine(RobloxInstallPath, "version.txt");
                if (File.Exists(versionFile))
                {
                    InstalledVersionText.Text = File.ReadAllText(versionFile).Trim();
                    Log($"Roblox installed: {InstalledVersionText.Text}");
                }
                else
                {
                    InstalledVersionText.Text = "Unknown version";
                    Log("Roblox found but version unknown");
                }
            }
            else
            {
                InstalledVersionText.Text = "Not installed";
                Log("Roblox not installed");
            }
        }

        private async void CheckForUpdates()
        {
            try
            {
                string repo = RepoUrlBox.Text.Trim();
                if (string.IsNullOrEmpty(repo)) return;

                Log($"Checking GitHub for updates: {repo}");
                
                string apiUrl = $"{GITHUB_API}{repo}/releases/latest";
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        string tag = root.GetProperty("tag_name").GetString();
                        string published = root.GetProperty("published_at").GetString();
                        
                        LatestReleaseText.Text = $"{tag} ({published})";
                        VersionText.Text = tag;
                        UpdateInfoText.Text = $"Update available: {tag}";
                        UpdateInfoText.Foreground = Brushes.LightGreen;
                    }
                }
                else
                {
                    Log("No releases found");
                    LatestReleaseText.Text = "No releases";
                }
            }
            catch (Exception ex)
            {
                Log($"Update check failed: {ex.Message}");
                LatestReleaseText.Text = "Check failed";
            }
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => CheckForUpdates());
        }

        private async void DownloadFromGitHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string repo = RepoUrlBox.Text.Trim();
                Log($"Downloading from GitHub: {repo}");

                string apiUrl = $"{GITHUB_API}{repo}/releases/latest";
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    Log("Failed to get release info");
                    MessageBox.Show("Failed to get release info from GitHub!");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement assets = root.GetProperty("assets");
                    
                    string downloadUrl = null;
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        string assetName = asset.GetProperty("name").GetString();
                        if (assetName.Contains("Roblox") || assetName.Contains("roblox"))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }

                    if (downloadUrl == null)
                    {
                        foreach (JsonElement asset in assets.EnumerateArray())
                        {
                            string assetName = asset.GetProperty("name").GetString();
                            if (assetName.EndsWith(".zip"))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                break;
                            }
                        }
                    }

                    if (downloadUrl == null)
                    {
                        Log("No downloadable assets found");
                        MessageBox.Show("No downloadable assets found in the release!");
                        return;
                    }

                    Log($"Downloading from: {downloadUrl}");
                    StatusText.Text = "Downloading from GitHub...";
                    StatusText.Foreground = Brushes.Orange;

                    string tempFile = Path.Combine(Path.GetTempPath(), "iStrapperDownload.zip");
                    
                    using (var downloadResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        downloadResponse.EnsureSuccessStatusCode();
                        long totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
                        
                        using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                        using (Stream stream = await downloadResponse.Content.ReadAsStreamAsync())
                        {
                            byte[] buffer = new byte[8192];
                            int bytesRead;
                            long totalRead = 0;

                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fs.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (totalBytes > 0)
                                {
                                    double percentage = (double)totalRead / totalBytes * 100;
                                    ProgressBar.Value = percentage;
                                    StatusText.Text = $"Downloading... {percentage:F0}%";
                                }
                            }
                        }
                    }

                    Log("Download complete!");
                    StatusText.Text = "Download complete!";
                    StatusText.Foreground = Brushes.LightGreen;

                    if (tempFile.EndsWith(".zip"))
                    {
                        string extractPath = Path.Combine(Path.GetTempPath(), "iStrapperExtracted");
                        if (Directory.Exists(extractPath))
                            Directory.Delete(extractPath, true);
                        
                        ZipFile.ExtractToDirectory(tempFile, extractPath);
                        Log($"Extracted to {extractPath}");
                        
                        string installer = FindInstaller(extractPath);
                        if (installer != null)
                        {
                            var result = MessageBox.Show("Download complete! Install now?", "Install", 
                                MessageBoxButton.YesNo, MessageBoxImage.Question);
                            
                            if (result == MessageBoxResult.Yes)
                            {
                                InstallRoblox(installer);
                            }
                        }
                    }
                    else
                    {
                        var result = MessageBox.Show("Download complete! Install now?", "Install", 
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            InstallRoblox(tempFile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Download failed: {ex.Message}");
                StatusText.Text = "Download failed";
                StatusText.Foreground = Brushes.Red;
                MessageBox.Show($"Download failed: {ex.Message}");
            }
        }

        private string FindInstaller(string directory)
        {
            string[] patterns = { "*.exe", "*.msi" };
            
            foreach (string pattern in patterns)
            {
                string[] files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            
            return null;
        }

        private void InstallRoblox_Click(object sender, RoutedEventArgs e)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "iStrapperDownload.zip");
            if (File.Exists(tempFile))
            {
                string extractPath = Path.Combine(Path.GetTempPath(), "iStrapperExtracted");
                string installer = FindInstaller(extractPath);
                
                if (installer != null)
                {
                    InstallRoblox(installer);
                }
                else
                {
                    InstallRoblox(tempFile);
                }
            }
            else
            {
                Log("No downloaded file found. Download first!");
                MessageBox.Show("Please download from GitHub first!");
            }
        }

        private void InstallRoblox(string installerPath)
        {
            try
            {
                Log($"Installing from: {installerPath}");
                StatusText.Text = "Installing...";
                StatusText.Foreground = Brushes.Orange;

                Directory.CreateDirectory(RobloxInstallPath);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"-install -channel LIVE -path \"{RobloxInstallPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                }

                Thread.Sleep(2000);

                if (Directory.Exists(RobloxInstallPath))
                {
                    string versionFile = Path.Combine(RobloxInstallPath, "version.txt");
                    if (File.Exists(versionFile))
                    {
                        InstalledVersionText.Text = File.ReadAllText(versionFile).Trim();
                    }

                    Log("Installation complete!");
                    StatusText.Text = "Installation complete!";
                    StatusText.Foreground = Brushes.LightGreen;
                    MessageBox.Show("Roblox installed successfully!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("Installation may have failed");
                    MessageBox.Show("Installation may have failed!", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }

                ProgressBar.Value = 100;
            }
            catch (Exception ex)
            {
                Log($"Installation failed: {ex.Message}");
                StatusText.Text = "Installation failed";
                StatusText.Foreground = Brushes.Red;
                MessageBox.Show($"Installation failed: {ex.Message}");
            }
        }

        private void LaunchRoblox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply mods first
                ApplyMods_Click(sender, e);
                
                // Apply FPS settings
                ApplyFpsSettings();
                
                // iOS compatibility
                if (EnableiOSCheck.IsChecked == true)
                {
                    ApplyiOSCompatibility();
                }

                string playerPath = Path.Combine(RobloxInstallPath, "RobloxPlayerBeta.exe");
                if (!File.Exists(playerPath))
                    playerPath = Path.Combine(RobloxInstallPath, "RobloxPlayer.exe");

                if (!File.Exists(playerPath))
                {
                    Log("Roblox player not found!");
                    MessageBox.Show("Roblox player not found. Please install first!");
                    return;
                }

                Log($"Launching Roblox: {playerPath}");
                StatusText.Text = "Launching Roblox...";
                StatusText.Foreground = Brushes.Orange;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = playerPath,
                    Arguments = "-bootstrapper",
                    UseShellExecute = true
                };

                Process.Start(psi);
                Log("Roblox launched!");

                // Auto-inject if DLL selected
                if (!string.IsNullOrEmpty(DllPathBox.Text) && File.Exists(DllPathBox.Text))
                {
                    Thread.Sleep(3000);
                    InjectDll_Click(sender, e);
                }

                StatusText.Text = "Roblox running";
                StatusText.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                Log($"Launch failed: {ex.Message}");
                MessageBox.Show($"Launch failed: {ex.Message}");
            }
        }

        private void ApplyiOSCompatibility()
        {
            try
            {
                Log("Applying iOS compatibility settings...");
                
                string clientSettings = Path.Combine(RobloxInstallPath, "ClientSettings", "ClientAppSettings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(clientSettings));
                
                var settings = new Dictionary<string, object>
                {
                    ["FFlagDebugGraphicsPreferD3D11"] = false,
                    ["FFlagRenderJobManagement"] = true,
                    ["FFlagDebugGraphicsDisableQuads"] = true,
                    ["FFlagFastGPULightCulling3"] = true,
                    ["DFIntTaskSchedulerTargetFps"] = _unlockFps ? 120 : _fpsLimit,
                    ["FFlagEnableInGameMenu"] = true,
                    ["FFlagEnableTouchEvents"] = true,
                    ["FFlagEnableTouchUI"] = true
                };
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(clientSettings, json);
                
                Log($"iOS compatibility applied for {iOSDeviceBox.SelectedItem} (iOS {iOSVersionBox.Text})");
            }
            catch (Exception ex)
            {
                Log($"Failed to apply iOS compatibility: {ex.Message}");
            }
        }

        private void InjectDll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(DllPathBox.Text))
            {
                MessageBox.Show("Please select a DLL to inject!");
                return;
            }

            if (!File.Exists(DllPathBox.Text))
            {
                MessageBox.Show("DLL file not found!");
                return;
            }

            Process[] procs = Process.GetProcessesByName("RobloxPlayerBeta");
            if (procs.Length == 0)
                procs = Process.GetProcessesByName("RobloxPlayer");

            if (procs.Length == 0)
            {
                Log("Roblox is not running!");
                MessageBox.Show("Roblox is not running!");
                return;
            }

            Process roblox = procs[0];
            Log($"Injecting into Roblox (PID: {roblox.Id})...");
            StatusText.Text = $"Injecting into PID {roblox.Id}...";
            StatusText.Foreground = Brushes.Orange;

            try
            {
                string dllPath = Path.GetFullPath(DllPathBox.Text);
                IntPtr hProcess = OpenProcess(0x1F0FFF, false, roblox.Id);
                
                if (hProcess == IntPtr.Zero)
                {
                    Log($"Failed to open process. Error: {Marshal.GetLastWin32Error()}");
                    MessageBox.Show($"Failed to open process. Error: {Marshal.GetLastWin32Error()}");
                    return;
                }

                byte[] pathBytes = Encoding.ASCII.GetBytes(dllPath + '\0');
                IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)pathBytes.Length, 0x3000, 0x40);
                
                if (remoteMem == IntPtr.Zero)
                {
                    Log("VirtualAllocEx failed!");
                    CloseHandle(hProcess);
                    return;
                }

                WriteProcessMemory(hProcess, remoteMem, pathBytes, (uint)pathBytes.Length, out _);

                IntPtr loadLib = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLib, remoteMem, 0, IntPtr.Zero);

                if (hThread != IntPtr.Zero)
                {
                    Log("Injection successful!");
                    StatusText.Text = "Injection successful!";
                    StatusText.Foreground = Brushes.LightGreen;
                }
                else
                {
                    Log($"Injection failed! Error: {Marshal.GetLastWin32Error()}");
                    MessageBox.Show($"Injection failed! Error: {Marshal.GetLastWin32Error()}");
                }

                CloseHandle(hThread);
                CloseHandle(hProcess);
            }
            catch (Exception ex)
            {
                Log($"Injection error: {ex.Message}");
                MessageBox.Show($"Injection error: {ex.Message}");
            }
        }

        private void KillRoblox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("RobloxPlayerBeta");
                if (procs.Length == 0)
                    procs = Process.GetProcessesByName("RobloxPlayer");

                foreach (Process proc in procs)
                {
                    proc.Kill();
                    Log($"Killed Roblox (PID: {proc.Id})");
                }

                if (procs.Length == 0)
                {
                    Log("No Roblox process found");
                }
                else
                {
                    StatusText.Text = "Roblox killed";
                    StatusText.Foreground = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to kill Roblox: {ex.Message}");
            }
        }

        private void BrowseDll_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Title = "Select DLL to Inject"
            };

            if (dialog.ShowDialog() == true)
            {
                DllPathBox.Text = dialog.FileName;
                Log($"Selected DLL: {dialog.FileName}");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveConfig();
            base.OnClosing(e);
        }
    }
}
