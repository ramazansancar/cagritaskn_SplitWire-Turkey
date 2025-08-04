using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic; // Added missing import

namespace SplitWireTurkey.Services
{
    public class WireGuardService
    {
        private readonly string _wgcfPath;
        private readonly string _resDir;

        public WireGuardService()
        {
            _resDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res");
            _wgcfPath = Path.Combine(_resDir, "wgcf.exe");
            
            if (!Directory.Exists(_resDir))
                Directory.CreateDirectory(_resDir);
        }

        public async Task<bool> CreateProfileAsync(string[] extraFolders = null)
        {
            try
            {
                var wgcfPath = await DownloadWgcfAsync();
                if (string.IsNullOrEmpty(wgcfPath))
                {
                    System.Windows.MessageBox.Show("wgcf.exe indirilemedi.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Remove existing account file if it exists
                var accountFile = Path.Combine(_resDir, "wgcf-account.toml");
                if (File.Exists(accountFile))
                {
                    try { File.Delete(accountFile); } catch { }
                }

                // Register with wgcf
                var registerResult = await ExecuteCommandAsync(_wgcfPath, "register --accept-tos");
                if (registerResult != 0)
                {
                    MessageBox.Show($"Register işlemi başarısız oldu. Return code: {registerResult}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Generate profile
                var generateResult = await ExecuteCommandAsync(_wgcfPath, "generate");
                if (generateResult != 0)
                {
                    MessageBox.Show("Generate işlemi başarısız oldu.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Modify configuration
                var profilePath = Path.Combine(_resDir, "wgcf-profile.conf");
                if (File.Exists(profilePath))
                {
                    return await ModifyConfigurationAsync(profilePath, extraFolders);
                }
                else
                {
                    MessageBox.Show("Profil dosyası bulunamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Profil oluşturulurken hata: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<bool> ModifyConfigurationAsync(string profilePath, string[] extraFolders)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(profilePath);
                var newLines = new List<string>();
                var username = Environment.UserName;
                var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

                var appPaths = new List<string>
                {
                    discordPath,
                    "discord",
                    "roblox",
                    "Discord.exe",
                    "Update.exe",
                    "RobloxPlayerBeta.exe",
                    "RobloxPlayerInstaller.exe"
                };

                if (extraFolders != null)
                {
                    foreach (var folder in extraFolders)
                    {
                        if (!string.IsNullOrWhiteSpace(folder))
                            appPaths.Add(folder.Trim());
                    }
                }

                var allowedAppsLine = $"AllowedApps = {string.Join(", ", appPaths)}";

                foreach (var line in lines)
                {
                    newLines.Add(line);
                    if (line.Trim().StartsWith("Endpoint"))
                    {
                        newLines.Add(allowedAppsLine);
                    }
                }

                await File.WriteAllLinesAsync(profilePath, newLines);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Konfigürasyon düzenlenirken hata: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<int> ExecuteCommandAsync(string command, string arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = arguments,
                        WorkingDirectory = _resDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = startInfo };
                    process.Start();
                    process.WaitForExit();
                    return process.ExitCode;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Command execution failed: {ex.Message}");
                    return -1;
                }
            });
        }

        public string GetConfigPath()
        {
            return Path.Combine(_resDir, "wgcf-profile.conf");
        }

        private async Task<string> DownloadWgcfAsync()
        {
            try
            {
                // Check if wgcf.exe already exists and is not too old (7 days)
                if (File.Exists(_wgcfPath))
                {
                    var fileInfo = new FileInfo(_wgcfPath);
                    if (DateTime.Now.Subtract(fileInfo.CreationTime).TotalDays < 7)
                    {
                        return _wgcfPath;
                    }
                }

                // Download latest release info from GitHub
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "SplitWire-Turkey");
                    
                    // Get latest release info
                    var releasesUrl = "https://api.github.com/repos/ViRb3/wgcf/releases/latest";
                    var releasesResponse = await client.GetStringAsync(releasesUrl);
                    
                    // Parse JSON to find Windows AMD64 asset
                    var assetMatch = System.Text.RegularExpressions.Regex.Match(releasesResponse, 
                        @"""browser_download_url"":\s*""([^""]*wgcf_[^""]*_windows_amd64[^""]*)""");
                    
                    if (!assetMatch.Success)
                    {
                        System.Windows.MessageBox.Show("Windows AMD64 sürümü bulunamadı.", 
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }

                    var downloadUrl = assetMatch.Groups[1].Value;
                    
                    // Download the file
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    
                    using (var fileStream = File.Create(_wgcfPath))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                return _wgcfPath;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"wgcf.exe indirilirken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
    }
} 