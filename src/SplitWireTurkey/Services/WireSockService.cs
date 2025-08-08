using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SplitWireTurkey.Services
{
    public class WireSockService
    {
        public string FindWireSockPath()
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (!drive.IsReady) continue;

                var paths = new[]
                {
                    // Yeni sürüm (WireSock Secure Connect)
                    Path.Combine(drive.Name, "Program Files", "WireSock Secure Connect", "bin", "wiresock-client.exe"),
                    Path.Combine(drive.Name, "Program Files (x86)", "WireSock Secure Connect", "bin", "wiresock-client.exe"),
                    // Eski sürüm (WireSock VPN Client) - 1.4.7.1
                    Path.Combine(drive.Name, "Program Files", "WireSock VPN Client", "bin", "wiresock-client.exe"),
                    Path.Combine(drive.Name, "Program Files (x86)", "WireSock VPN Client", "bin", "wiresock-client.exe")
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                        return path;
                }
            }
            return null;
        }

        public string FindWireSockPathOptimized()
        {
            // Sadece C, D ve E sürücülerini tara
            var targetDrives = new[] { "C:", "D:", "E:" };
            
            foreach (var driveLetter in targetDrives)
            {
                try
                {
                    var drive = new DriveInfo(driveLetter);
                    if (!drive.IsReady) continue;

                    var paths = new[]
                    {
                        // Yeni sürüm (WireSock Secure Connect)
                        Path.Combine(drive.Name, "Program Files", "WireSock Secure Connect", "bin", "wiresock-client.exe"),
                        Path.Combine(drive.Name, "Program Files (x86)", "WireSock Secure Connect", "bin", "wiresock-client.exe"),
                        // Eski sürüm (WireSock VPN Client) - 1.4.7.1
                        Path.Combine(drive.Name, "Program Files", "WireSock VPN Client", "bin", "wiresock-client.exe"),
                        Path.Combine(drive.Name, "Program Files (x86)", "WireSock VPN Client", "bin", "wiresock-client.exe")
                    };

                    foreach (var path in paths)
                    {
                        if (File.Exists(path))
                            return path;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Sürücü {driveLetter} kontrol edilirken hata: {ex.Message}");
                    continue;
                }
            }
            return null;
        }

        public async Task<bool> InstallServiceAsync(string configPath)
        {
            try
            {
                var wiresockExe = FindWireSockPath();
                if (string.IsNullOrEmpty(wiresockExe) || !File.Exists(wiresockExe))
                {
                    System.Windows.MessageBox.Show("WireSock kurulumu bulunamadı. Birazdan indirme işlemi için onay alınacak.", 
                        "WireSock Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                var installArgs = $"install -start-type 2 -config \"{configPath}\" -log-level none";
                var result = await ExecuteCommandAsync(wiresockExe, installArgs);

                if (result == 0)
                {
                    // Hizmet kurulduktan sonra başlatmayı dene
                    var startResult = await ExecuteCommandAsync("net", "start wiresock-client-service");
                    if (startResult != 0)
                    {
                        // net start başarısız olursa sc start ile dene
                        startResult = await ExecuteCommandAsync("sc", "start wiresock-client-service");
                    }

                    if (startResult == 0)
                    {
                        // Completion message removed - now handled in main window
                        return true;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("WireSock hizmeti kuruldu ancak başlatılamadı. Hizmet zaten çalışıyor olabilir. Manuel olarak başlatmayı deneyin.", 
                            "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true; // Still return true since service was installed
                    }
                }
                else
                {
                    // Check if service was actually installed despite the error
                    var serviceQueryResult = await ExecuteCommandAsync("sc", "query wiresock-client-service");
                    if (serviceQueryResult == 0)
                    {
                        System.Windows.MessageBox.Show($"WireSock hizmeti kuruldu (Return code: {result}). Hizmet zaten mevcut olabilir.", 
                            "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"Kurulum başarısız. Return code: {result}", 
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hizmet kurulumu sırasında hata: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> RemoveServiceAsync()
        {
            try
            {
                var wiresockExe = FindWireSockPath();
                if (string.IsNullOrEmpty(wiresockExe) || !File.Exists(wiresockExe))
                {
                    System.Windows.MessageBox.Show("WireSock kurulumu bulunamadı.", 
                        "WireSock Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                var result = await ExecuteCommandAsync(wiresockExe, "uninstall");

                if (result == 0)
                {
                    return true;
                }
                else
                {
                    System.Windows.MessageBox.Show($"Hizmet kaldırılamadı. Return code: {result}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hizmet kaldırılamadı: {ex.Message}", 
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

        public bool IsWireSockInstalled()
        {
            return !string.IsNullOrEmpty(FindWireSockPath());
        }

        public async Task<bool> IsWireSockInstalledAsync()
        {
            return await Task.Run(() => !string.IsNullOrEmpty(FindWireSockPathOptimized()));
        }

        public bool IsLatestWireSockInstalled()
        {
            // Sadece C, D ve E sürücülerini tara (optimize edilmiş)
            var targetDrives = new[] { "C:", "D:", "E:" };
            
            foreach (var driveLetter in targetDrives)
            {
                try
                {
                    var drive = new DriveInfo(driveLetter);
                    if (!drive.IsReady) continue;

                    var paths = new[]
                    {
                        // Sadece yeni sürüm (WireSock Secure Connect)
                        Path.Combine(drive.Name, "Program Files", "WireSock Secure Connect", "bin", "wiresock-client.exe"),
                        Path.Combine(drive.Name, "Program Files (x86)", "WireSock Secure Connect", "bin", "wiresock-client.exe")
                    };

                    foreach (var path in paths)
                    {
                        if (File.Exists(path))
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IsLatestWireSockInstalled - Sürücü {driveLetter} kontrol edilirken hata: {ex.Message}");
                    continue;
                }
            }
            return false;
        }

        public async Task<bool> IsServiceRunningAsync()
        {
            try
            {
                var result = await ExecuteCommandAsync("sc", "query wiresock-client-service");
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> StartServiceAsync()
        {
            try
            {
                var result = await ExecuteCommandAsync("net", "start wiresock-client-service");
                if (result != 0)
                {
                    result = await ExecuteCommandAsync("sc", "start wiresock-client-service");
                }
                return result == 0;
            }
            catch
            {
                return false;
            }
        }
    }
} 