using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;
using SplitWireTurkey.Services;
using System.Runtime.InteropServices;

namespace SplitWireTurkey
{
    public partial class MainWindow : Window
    {
        // Windows Firewall API P/Invoke tanımları
        [DllImport("netapi32.dll", SetLastError = true)]
        private static extern int NetApiBufferFree(IntPtr Buffer);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_TCB_NAME = "SeTcbPrivilege";

        private readonly WireGuardService _wireGuardService;
        private readonly WireSockService _wireSockService;
        private readonly List<string> _folders;

        public MainWindow()
        {
            InitializeComponent();
            
            _wireGuardService = new WireGuardService();
            _wireSockService = new WireSockService();
            _folders = new List<string>();
            
            UpdateWireSockStatus();
        }

        private void UpdateWireSockStatus()
        {
            if (_wireSockService.IsWireSockInstalled())
            {
                txtWiresockStatus.Text = "";
                txtWiresockStatus.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                txtWiresockStatus.Text = "WireSock yüklü değil!";
                txtWiresockStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Hızlı kurulum başlatmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                // Önce ProxiFyreService ve ByeDPI hizmetlerini durdur ve kaldır
                await StopAndRemoveServicesAsync();
                
                // Önce tüm hizmetleri kaldır
                await RemoveAllServicesAsync();

                if (!_wireSockService.IsLatestWireSockInstalled())
                {
                    var downloadResult = System.Windows.MessageBox.Show("WireSock'un son sürümü yüklü değil. WireSock kurulum dosyasını indirip çalıştırmak ister misiniz?", 
                        "WireSock Son Sürüm Yüklü Değil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (downloadResult == MessageBoxResult.Yes)
                    {
                        await DownloadAndInstallWireSock();
                    }
                    else
                    {
                        ShowLoading(false);
                        return;
                    }
                }

                await PerformFastSetup();
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async void BtnAlternativeSetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Alternatif kurulum başlatmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                // Önce ProxiFyreService ve ByeDPI hizmetlerini durdur ve kaldır
                await StopAndRemoveServicesAsync();
                
                // Önce tüm hizmetleri kaldır
                await RemoveAllServicesAsync();

                await PerformAlternativeSetup();
            }
            finally
            {
                ShowLoading(false);
            }
        }



        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Uygulamadan çıkmak istediğinize emin misiniz?", 
                "Çıkış", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "SplitWire-Turkey © 2025 Çağrı Taşkın\n\n" +
                "Daha fazla bilgi ve kaynak kodu için Github sayfasını ziyaret etmek ister misiniz?",
                "Bilgi", MessageBoxButton.YesNo, MessageBoxImage.Information);
            
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/cagritaskn/SplitWire-Turkey",
                    UseShellExecute = true
                });
            }
        }

        private async void BtnRemoveAllServices_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "wiresock-client-service, GoodbyeDPI, WinDivert, ByeDPI, ProxiFyreService hizmetlerini durdurup kaldırmak ve Discord klasöründeki drover dosyalarını silmek istediğinizden emin misiniz?",
                "Hizmetleri Kaldır", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                var services = new[] { "wiresock-client-service", "GoodbyeDPI", "WinDivert", "ByeDPI", "ProxiFyreService" };
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "Tüm hizmetler durduruluyor ve kaldırılıyor...\n");
                
                foreach (var service in services)
                {
                    // Hizmeti durdur
                    ExecuteCommand("sc", $"stop {service}");
                    File.AppendAllText(logPath, $"{service} hizmeti durduruldu.\n");
                    
                    // Hizmeti sil
                    ExecuteCommand("sc", $"delete {service}");
                    File.AppendAllText(logPath, $"{service} hizmeti silindi.\n");
                }

                // Windows Firewall kurallarını da temizle
                File.AppendAllText(logPath, "Windows Firewall kuralları temizleniyor...\n");
                await RemoveFirewallRulesAsync();
                
                // Drover dosyalarını temizle
                File.AppendAllText(logPath, "Discord klasöründeki drover dosyaları temizleniyor...\n");
                await CleanupDroverFilesAsync();
                
                System.Windows.MessageBox.Show("Tüm hizmetler, firewall kuralları ve drover dosyaları başarıyla kaldırıldı.", 
                    "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void BtnHeartInfo_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI metodu için Bal Porsuğu'na teşekkürler. YouTube kanalını ziyaret etmek için Evet'e basın.",
                "Bal Porsuğu", MessageBoxButton.YesNo, MessageBoxImage.Information);
            
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.youtube.com/@sauali",
                    UseShellExecute = true
                });
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Klasör Seç"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var folder = dialog.SelectedPath;
                if (!_folders.Contains(folder))
                {
                    _folders.Add(folder);
                    UpdateFolderList();
                }
            }
        }

        private void BtnClearFolders_Click(object sender, RoutedEventArgs e)
        {
            _folders.Clear();
            UpdateFolderList();
        }

        private void UpdateFolderList()
        {
            lstFolders.Items.Clear();
            foreach (var folder in _folders)
            {
                lstFolders.Items.Add(folder);
            }
        }

        private async void BtnCustomSetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Özelleştirilmiş hızlı kurulum başlatmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                // Önce ProxiFyreService ve ByeDPI hizmetlerini durdur ve kaldır
                await StopAndRemoveServicesAsync();
                
                // Önce tüm hizmetleri kaldır
                await RemoveAllServicesAsync();

                if (!_wireSockService.IsWireSockInstalled())
                {
                    var downloadResult = System.Windows.MessageBox.Show("WireSock yüklü değil. WireSock kurulum dosyasını indirip çalıştırmak ister misiniz?", 
                        "WireSock Yüklü Değil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (downloadResult == MessageBoxResult.Yes)
                    {
                        await DownloadAndInstallWireSock();
                    }
                    else
                    {
                        ShowLoading(false);
                        return;
                    }
                }

                await PerformCustomSetup();
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async void BtnGenerateConfig_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Özelleştirilmiş profil dosyası oluşturmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            await GenerateConfigOnly();
        }



        private async void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("WireSock hizmetini kaldırmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                var success = await _wireSockService.RemoveServiceAsync();
                if (success)
                {
                    System.Windows.MessageBox.Show("WireSock hizmeti başarıyla kaldırıldı.", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("WireSock hizmeti kaldırılamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async void BtnGoodbyeDPI_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("GoodbyeDPI ve WinDivert hizmetlerini kaldırmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                var success = await RemoveGoodbyeDPIServicesAsync();
                if (success)
                {
                    System.Windows.MessageBox.Show("GoodbyeDPI ve WinDivert hizmetleri başarıyla kaldırıldı.", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("Hizmetler kaldırılamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task<bool> RemoveGoodbyeDPIServicesAsync()
        {
            try
            {
                // Hizmetleri durdur
                var stopGoodbyeDPI = await ExecuteCommandAsync("net", "stop GoodbyeDPI");
                var stopWinDivert = await ExecuteCommandAsync("net", "stop WinDivert");
                
                // Hizmetleri kaldır
                var removeGoodbyeDPI = await ExecuteCommandAsync("sc", "delete GoodbyeDPI");
                var removeWinDivert = await ExecuteCommandAsync("sc", "delete WinDivert");
                
                return true; // Hata olsa bile true döndür çünkü hizmet zaten kaldırılmış olabilir
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GoodbyeDPI hizmet kaldırma hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RemoveAllServicesAsync()
        {
            try
            {
                System.Windows.MessageBox.Show("Kurulum öncesi mevcut hizmetler kaldırılıyor...", 
                    "Hizmet Temizliği", MessageBoxButton.OK, MessageBoxImage.Information);

                // WireSock hizmetini kaldır
                var wiresockRemoved = await _wireSockService.RemoveServiceAsync();
                
                // GoodbyeDPI ve WinDivert hizmetlerini kaldır
                var goodbyeDPIRemoved = await RemoveGoodbyeDPIServicesAsync();
                
                // Hizmetleri durdur (ek güvenlik için)
                await ExecuteCommandAsync("net", "stop wiresock-client-service");
                await ExecuteCommandAsync("net", "stop GoodbyeDPI");
                await ExecuteCommandAsync("net", "stop WinDivert");
                
                // Hizmetleri sil (ek güvenlik için)
                await ExecuteCommandAsync("sc", "delete wiresock-client-service");
                await ExecuteCommandAsync("sc", "delete GoodbyeDPI");
                await ExecuteCommandAsync("sc", "delete WinDivert");
                
                // Windows Firewall kurallarını da temizle
                await RemoveFirewallRulesAsync();
                
                System.Windows.MessageBox.Show("Tüm hizmetler ve firewall kuralları başarıyla kaldırıldı. Kuruluma devam ediliyor...", 
                    "Hizmet Temizliği Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hizmet kaldırma hatası: {ex.Message}");
                System.Windows.MessageBox.Show($"Hizmet kaldırma sırasında hata oluştu: {ex.Message}\nKuruluma devam ediliyor...", 
                    "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async Task PerformFastSetup()
        {
            ShowLoading(true);
            
            var logPath = GetLogPath();
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hızlı kurulum başlatılıyor...\n");
            
            try
            {
                // Drover dosyalarını temizle
                File.AppendAllText(logPath, "Drover dosyaları temizleniyor...\n");
                await CleanupDroverFilesAsync();
                
                // DNS ayarları
                File.AppendAllText(logPath, "DNS ayarları yapılıyor...\n");
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    File.AppendAllText(logPath, "DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }

                File.AppendAllText(logPath, "WireGuard profili oluşturuluyor...\n");
                var success = await _wireGuardService.CreateProfileAsync();
                
                if (success)
                {
                    File.AppendAllText(logPath, "WireGuard profili başarıyla oluşturuldu.\n");
                    var configPath = _wireGuardService.GetConfigPath();
                    File.AppendAllText(logPath, $"Konfigürasyon dosyası yolu: {configPath}\n");
                    
                    File.AppendAllText(logPath, "WireSock hizmeti kuruluyor...\n");
                    var serviceResult = await _wireSockService.InstallServiceAsync(configPath);
                    
                    if (serviceResult)
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti başarıyla kuruldu.\n");
                        File.AppendAllText(logPath, "Sistem yeniden başlatma mesajı gösteriliyor.\n");
                        ShowRestartMessage();
                    }
                    else
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti kurulamadı.\n");
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "WireGuard profili oluşturulamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Hızlı kurulum hatası: {ex.Message}\n");
            }
            finally
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hızlı kurulum tamamlandı.\n");
                ShowLoading(false);
            }
        }

        private async Task PerformCustomSetup()
        {
            ShowLoading(true);
            
            var logPath = GetLogPath();
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Özel kurulum başlatılıyor...\n");
            
            try
            {
                // Drover dosyalarını temizle
                File.AppendAllText(logPath, "Drover dosyaları temizleniyor...\n");
                await CleanupDroverFilesAsync();
                
                // DNS ayarları
                File.AppendAllText(logPath, "DNS ayarları yapılıyor...\n");
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    File.AppendAllText(logPath, "DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }

                var extraFolders = _folders.ToArray();
                File.AppendAllText(logPath, $"Ek klasörler: {string.Join(", ", extraFolders)}\n");
                File.AppendAllText(logPath, "WireGuard profili oluşturuluyor...\n");
                
                var success = await _wireGuardService.CreateProfileAsync(extraFolders);
                if (success)
                {
                    File.AppendAllText(logPath, "WireGuard profili başarıyla oluşturuldu.\n");
                    var configPath = _wireGuardService.GetConfigPath();
                    File.AppendAllText(logPath, $"Konfigürasyon dosyası yolu: {configPath}\n");
                    
                    File.AppendAllText(logPath, "WireSock hizmeti kuruluyor...\n");
                    var serviceResult = await _wireSockService.InstallServiceAsync(configPath);
                    
                    if (serviceResult)
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti başarıyla kuruldu.\n");
                        File.AppendAllText(logPath, "Sistem yeniden başlatma mesajı gösteriliyor.\n");
                        ShowRestartMessage();
                    }
                    else
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti kurulamadı.\n");
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "WireGuard profili oluşturulamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Özel kurulum hatası: {ex.Message}\n");
            }
            finally
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Özel kurulum tamamlandı.\n");
                ShowLoading(false);
            }
        }

        private async Task GenerateConfigOnly()
        {
            ShowLoading(true);
            
            var logPath = GetLogPath();
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Sadece konfigürasyon oluşturma başlatılıyor...\n");
            
            try
            {
                var extraFolders = _folders.ToArray();
                File.AppendAllText(logPath, $"Ek klasörler: {string.Join(", ", extraFolders)}\n");
                File.AppendAllText(logPath, "WireGuard profili oluşturuluyor...\n");
                
                var success = await _wireGuardService.CreateProfileAsync(extraFolders);
                
                if (success)
                {
                    var configPath = _wireGuardService.GetConfigPath();
                    File.AppendAllText(logPath, $"Profil dosyası başarıyla oluşturuldu: {configPath}\n");
                    System.Windows.MessageBox.Show($"Profil dosyası oluşturuldu:\n{configPath}", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    File.AppendAllText(logPath, "Profil dosyası oluşturulamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Konfigürasyon oluşturma hatası: {ex.Message}\n");
            }
            finally
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Konfigürasyon oluşturma tamamlandı.\n");
                ShowLoading(false);
            }
        }

        private async Task PerformAlternativeSetup()
        {
            ShowLoading(true);
            
            var logPath = GetLogPath();
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Alternatif kurulum başlatılıyor...\n");
            
            try
            {
                // Drover dosyalarını temizle
                File.AppendAllText(logPath, "Drover dosyaları temizleniyor...\n");
                await CleanupDroverFilesAsync();
                
                // DNS ayarları
                File.AppendAllText(logPath, "DNS ayarları yapılıyor...\n");
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    File.AppendAllText(logPath, "DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }

                // Önce mevcut WireSock hizmetini durdur ve kaldır
                File.AppendAllText(logPath, "Mevcut WireSock hizmeti kaldırılıyor...\n");
                var uninstallSuccess = await _wireSockService.RemoveServiceAsync();
                if (!uninstallSuccess)
                {
                    File.AppendAllText(logPath, "Mevcut WireSock hizmeti kaldırılamadı.\n");
                    System.Windows.MessageBox.Show("Mevcut WireSock hizmeti kaldırılamadı. Alternatif kurulum başlatılamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                File.AppendAllText(logPath, "Mevcut WireSock hizmeti başarıyla kaldırıldı.\n");

                // Mevcut WireSock kurulumunu kaldır - son sürüm setup dosyasını indir ve /uninstall /S ile kaldır
                File.AppendAllText(logPath, "Mevcut WireSock kurulumu kaldırılıyor...\n");
                var tempDir = Path.Combine(Path.GetTempPath(), "SplitWireTurkey");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                var currentSetupPath = Path.Combine(tempDir, "wiresock-current.exe");
                var currentDownloadUrl = "https://wiresock.net/_api/download-release.php?product=wiresock-secure-connect&platform=windows_x64&version=latest";

                try
                {
                    // Mevcut sürümü indir
                    File.AppendAllText(logPath, "Mevcut WireSock sürümü indiriliyor...\n");
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        var response = await client.GetAsync(currentDownloadUrl);
                        response.EnsureSuccessStatusCode();
                        
                        using (var fileStream = File.Create(currentSetupPath))
                        {
                            await response.Content.CopyToAsync(fileStream);
                        }
                    }

                    // Mevcut sürümü kaldır
                    File.AppendAllText(logPath, "Mevcut WireSock sürümü kaldırılıyor...\n");
                    var uninstallProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = currentSetupPath,
                            Arguments = "/uninstall /S",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            Verb = "runas"
                        }
                    };

                    uninstallProcess.Start();
                    await uninstallProcess.WaitForExitAsync();
                    File.AppendAllText(logPath, $"Mevcut WireSock sürümü kaldırma işlemi tamamlandı. Exit Code: {uninstallProcess.ExitCode}\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"Mevcut sürüm kaldırma hatası: {ex.Message}\n");
                    Debug.WriteLine($"Mevcut sürüm kaldırma hatası: {ex.Message}");
                    // Hata olsa bile devam et
                }

                // 1.4.7.1 sürümünü indir ve kur
                File.AppendAllText(logPath, "WireSock 1.4.7.1 sürümü indiriliyor...\n");
                var setupPath = Path.Combine(tempDir, "wiresock-legacy.msi");
                var resSetupPath = Path.Combine(Environment.CurrentDirectory, "res", "wiresock-vpn-client-x64-1.4.7.1.msi");

                bool downloadSuccess = false;
                
                // Önce /res klasöründen dosyayı kopyalamayı dene
                if (File.Exists(resSetupPath))
                {
                    File.AppendAllText(logPath, "1.4.7.1 sürümü /res klasöründen kopyalanıyor...\n");
                    File.Copy(resSetupPath, setupPath, true);
                    downloadSuccess = true;
                }
                else
                {
                    // /res klasöründe yoksa GitHub'dan indirmeyi dene
                    File.AppendAllText(logPath, "1.4.7.1 sürümü GitHub'dan indiriliyor...\n");
                    var downloadUrl = "https://github.com/cagritaskn/SplitWire-Turkey/raw/main/deploy/wiresock-vpn-client-x64-1.4.7.1.msi";
                    
                    try
                    {
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var response = await client.GetAsync(downloadUrl);
                            response.EnsureSuccessStatusCode();
                            
                            using (var fileStream = File.Create(setupPath))
                            {
                                await response.Content.CopyToAsync(fileStream);
                            }
                        }
                        downloadSuccess = true;
                        File.AppendAllText(logPath, "1.4.7.1 sürümü GitHub'dan başarıyla indirildi.\n");
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"GitHub'dan indirme hatası: {ex.Message}\n");
                        Debug.WriteLine($"GitHub'dan indirme hatası: {ex.Message}");
                        
                        // GitHub'dan da indirilemezse yerel dosyayı dene
                        File.AppendAllText(logPath, "Yerel dosya deneniyor...\n");
                        var localSetupPath = Path.Combine(Environment.CurrentDirectory, "wiresock-vpn-client-x64-1.4.7.1.msi");
                        if (File.Exists(localSetupPath))
                        {
                            File.Copy(localSetupPath, setupPath, true);
                            downloadSuccess = true;
                            File.AppendAllText(logPath, "Yerel dosya kullanılıyor.\n");
                            System.Windows.MessageBox.Show("GitHub'dan indirilemedi, yerel dosya kullanılıyor.", 
                                "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            File.AppendAllText(logPath, "1.4.7.1 sürümü bulunamadı.\n");
                            System.Windows.MessageBox.Show("1.4.7.1 sürümü bulunamadı.\nLütfen wiresock-vpn-client-x64-1.4.7.1.msi dosyasını /res klasörüne kopyalayın.", 
                                "Dosya Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }

                if (!downloadSuccess)
                {
                    File.AppendAllText(logPath, "1.4.7.1 sürümü indirilemedi.\n");
                    System.Windows.MessageBox.Show("1.4.7.1 sürümü indirilemedi.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                File.AppendAllText(logPath, "1.4.7.1 sürümü başarıyla indirildi.\n");
                // En uygun kurulum yolunu belirle
                var installPath = GetBestInstallPath();
                File.AppendAllText(logPath, $"Kurulum yolu: {installPath}\n");

                // MSI kurulum parametrelerini dene
                var msiArgsList = new[]
                {
                    $"/i \"{setupPath}\" TARGETDIR=\"{installPath}\" /quiet /norestart",
                    $"/i \"{setupPath}\" TARGETDIR=\"{installPath}\" /passive /norestart",
                    $"/i \"{setupPath}\" TARGETDIR=\"{installPath}\" /qn",
                    $"/i \"{setupPath}\" /quiet /norestart",
                    $"/i \"{setupPath}\" /passive /norestart",
                    $"/i \"{setupPath}\" /qn"
                };

                bool msiInstallSuccess = false;
                File.AppendAllText(logPath, "MSI sessiz kurulum deneniyor...\n");
                
                foreach (var msiArgs in msiArgsList)
                {
                    try
                    {
                        File.AppendAllText(logPath, $"MSI kurulum parametresi deneniyor: {msiArgs}\n");
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "msiexec",
                                Arguments = msiArgs,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                Verb = "runas" // Yönetici izni ile çalıştır
                            }
                        };

                        process.Start();
                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            File.AppendAllText(logPath, "MSI sessiz kurulum başarılı.\n");
                            System.Windows.MessageBox.Show($"WireSock 1.4.7.1 sürümü sessiz kurulumu tamamlandı.\nKuruluma devam ediliyor ...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                            msiInstallSuccess = true;
                            break;
                        }
                        else
                        {
                            File.AppendAllText(logPath, $"MSI kurulum başarısız. Exit Code: {process.ExitCode}\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"MSI kurulum hatası: {ex.Message}\n");
                        // Bu parametre çalışmadı, diğerini dene
                        continue;
                    }
                }

                // MSI kurulum başarısız olursa normal kurulumu dene
                if (!msiInstallSuccess)
                {
                    File.AppendAllText(logPath, "MSI sessiz kurulum başarısız, normal kurulum deneniyor...\n");
                    System.Windows.MessageBox.Show("Sessiz kurulum başarısız oldu. Normal kurulum başlatılıyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    var normalProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = setupPath,
                            UseShellExecute = true,
                            Verb = "runas"
                        }
                    };

                    normalProcess.Start();
                    await normalProcess.WaitForExitAsync();
                    
                    File.AppendAllText(logPath, "Normal kurulum tamamlandı.\n");
                    System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kurulumu tamamlandı. Kuruluma devam ediliyor ...", 
                        "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                
                UpdateWireSockStatus();
                File.AppendAllText(logPath, "WireSock durumu güncellendi.\n");
                
                // Kısa bir bekleme süresi ekle
                await Task.Delay(2000);
                
                // Hizmet kurulumu yap
                File.AppendAllText(logPath, "WireGuard profili oluşturuluyor...\n");
                var success = await _wireGuardService.CreateProfileAsync();
                if (success)
                {
                    File.AppendAllText(logPath, "WireGuard profili başarıyla oluşturuldu.\n");
                    var configPath = _wireGuardService.GetConfigPath();
                    File.AppendAllText(logPath, $"Konfigürasyon dosyası yolu: {configPath}\n");
                    
                    File.AppendAllText(logPath, "WireSock hizmeti kuruluyor...\n");
                    var serviceResult = await _wireSockService.InstallServiceAsync(configPath);
                    
                    if (serviceResult)
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti başarıyla kuruldu.\n");
                        ShowRestartMessage();
                    }
                    else
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti kurulamadı.\n");
                        System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kuruldu ancak hizmet başlatılamadı. Manuel olarak başlatmayı deneyin.", 
                            "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "WireGuard profili oluşturulamadı.\n");
                    System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kuruldu ancak profil oluşturulamadı.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Alternatif kurulum hatası: {ex.Message}\n");
                System.Windows.MessageBox.Show($"Alternatif kurulum sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Alternatif kurulum tamamlandı.\n");
                ShowLoading(false);
            }
        }

        private void ShowRestartMessage()
        {
            var result = System.Windows.MessageBox.Show(
                "Kurulum başarıyla tamamlandı. Değişikliklerin uygulanabilmesi için sisteminizi yeniden başlatın. Şimdi yeniden başlatmak için Evet'e tıklayın. Daha sonra yeniden başlatmak için Hayır'a tıklayın.",
                "Kurulum Tamamlandı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                // Şimdi yeniden başlat
                RestartSystem();
            }
            // No seçilirse sadece mesaj kutusu kapanır
        }

        private void RestartSystem()
        {
            try
            {
                // 5 saniye bekle ve sistemi yeniden başlat
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 5 /c \"SplitWire-Turkey kurulumu tamamlandı. Sistem yeniden başlatılıyor...\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Sistem yeniden başlatılamadı: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ShowLoading(bool show)
        {
            loadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task DownloadAndInstallWireSock()
        {
            try
            {
                ShowLoading(true);
                
                // Temp klasörü oluştur
                var tempDir = Path.Combine(Path.GetTempPath(), "SplitWireTurkey");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                var setupPath = Path.Combine(tempDir, "wiresock-setup.exe");
                var downloadUrl = "https://wiresock.net/_api/download-release.php?product=wiresock-secure-connect&platform=windows_x64&version=latest";

                // Dosyayı indir
                using (var client = new System.Net.Http.HttpClient())
                {
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    
                    using (var fileStream = File.Create(setupPath))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                // En uygun kurulum yolunu belirle
                var installPath = GetBestInstallPath();

                // Farklı sessiz kurulum parametrelerini dene
                var silentArgsList = new[]
                {
                    $"/S /D={installPath}",
                    $"/SILENT /D={installPath}",
                    $"/VERYSILENT /D={installPath}",
                    "/S",
                    "/SILENT",
                    "/VERYSILENT"
                };

                bool silentInstallSuccess = false;
                
                foreach (var silentArgs in silentArgsList)
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = setupPath,
                                Arguments = silentArgs,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };

                        process.Start();
                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            System.Windows.MessageBox.Show($"WireSock Secure Connect sessiz kurulumu tamamlandı.\nKuruluma devam ediliyor...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                            silentInstallSuccess = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Bu parametre çalışmadı, diğerini dene
                        continue;
                    }
                }

                // Sessiz kurulum başarısız olursa normal kurulumu dene
                if (!silentInstallSuccess)
                {
                    System.Windows.MessageBox.Show("Sessiz kurulum başarısız oldu. Normal kurulum başlatılıyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    var normalProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = setupPath,
                            UseShellExecute = true
                        }
                    };

                    normalProcess.Start();
                    await normalProcess.WaitForExitAsync();
                    
                    System.Windows.MessageBox.Show("WireSock Secure Connect kurulumu tamamlandı. Kuruluma devam ediliyor ...", 
                        "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Kurulum tamamlandıktan sonra WiresockConnect.exe'yi kapat
                var wiresockProcesses = Process.GetProcessesByName("WiresockConnect");
                foreach (var proc in wiresockProcesses)
                {
                    try
                    {
                        proc.Kill();
                    }
                    catch { }
                }
                
                UpdateWireSockStatus();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"WireSock Secure Connect kurulumu sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private string GetBestInstallPath()
        {
            try
            {
                // Mevcut sürücüleri al
                var drives = DriveInfo.GetDrives();
                var availableDrives = drives.Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();

                if (!availableDrives.Any())
                {
                    System.Windows.MessageBox.Show("Hiçbir sabit disk bulunamadı!", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return @"C:\Program Files\WireSock Secure Connect";
                }

                // Öncelik sırası: C:, D:, E:, vs.
                var preferredDrives = new[] { "C:", "D:", "E:", "F:", "G:", "H:", "I:", "J:" };
                
                foreach (var driveLetter in preferredDrives)
                {
                    var drive = availableDrives.FirstOrDefault(d => 
                        d.Name.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase));
                    
                    if (drive != null)
                    {
                        // Sürücüde yeterli alan var mı kontrol et (en az 100 MB)
                        if (drive.AvailableFreeSpace < 100 * 1024 * 1024) // 100 MB
                        {
                            continue; // Bu sürücüde yeterli alan yok, diğerini dene
                        }

                        // Program Files klasörünü kontrol et
                        var programFilesPath = Path.Combine(drive.Name, "Program Files");
                        var programFilesX86Path = Path.Combine(drive.Name, "Program Files (x86)");
                        
                        // Program Files klasörü varsa kullan
                        if (Directory.Exists(programFilesPath))
                        {
                            // Sadece mevcut WireSock Secure Connect kurulumunu kontrol et
                            var existingSecureConnectPath = Path.Combine(programFilesPath, "WireSock Secure Connect");
                            
                            if (Directory.Exists(existingSecureConnectPath))
                            {
                                System.Windows.MessageBox.Show($"Mevcut WireSock Secure Connect kurulumu bulundu. Kurulum şu konuma yapılacak:\n{existingSecureConnectPath}", 
                                    "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                                return existingSecureConnectPath;
                            }

                            // Mevcut kurulum yoksa yeni WireSock Secure Connect klasörünü kullan
                            var newInstallPath = Path.Combine(programFilesPath, "WireSock Secure Connect");
                            System.Windows.MessageBox.Show($"WireSock Secure Connect kurulumu şu konuma yapılacak:\n{newInstallPath}", 
                                "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                            return newInstallPath;
                        }
                        else if (Directory.Exists(programFilesX86Path))
                        {
                            // Sadece mevcut WireSock Secure Connect kurulumunu kontrol et
                            var existingSecureConnectPath = Path.Combine(programFilesX86Path, "WireSock Secure Connect");
                            
                            if (Directory.Exists(existingSecureConnectPath))
                            {
                                System.Windows.MessageBox.Show($"Mevcut WireSock Secure Connect kurulumu bulundu. Kurulum şu konuma yapılacak:\n{existingSecureConnectPath}", 
                                    "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                                return existingSecureConnectPath;
                            }

                            // Mevcut kurulum yoksa yeni WireSock Secure Connect klasörünü kullan
                            var newInstallPath = Path.Combine(programFilesX86Path, "WireSock Secure Connect");
                            System.Windows.MessageBox.Show($"WireSock Secure Connect kurulumu şu konuma yapılacak:\n{newInstallPath}", 
                                "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                            return newInstallPath;
                        }
                        else
                        {
                            // Program Files klasörü yoksa oluştur
                            try
                            {
                                Directory.CreateDirectory(programFilesPath);
                                var newInstallPath = Path.Combine(programFilesPath, "WireSock Secure Connect");
                                System.Windows.MessageBox.Show($"Program Files klasörü oluşturuldu ve WireSock Secure Connect kurulumu şu konuma yapılacak:\n{newInstallPath}", 
                                    "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Information);
                                return newInstallPath;
                            }
                            catch
                            {
                                var fallbackInstallPath = Path.Combine(drive.Name, "WireSock Secure Connect");
                                System.Windows.MessageBox.Show($"Program Files klasörü oluşturulamadı. WireSock Secure Connect kurulumu şu konuma yapılacak:\n{fallbackInstallPath}", 
                                    "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return fallbackInstallPath;
                            }
                        }
                    }
                }

                // Tercih edilen sürücüler bulunamadıysa ilk uygun sürücüyü kullan
                var fallbackDrive = availableDrives.FirstOrDefault();
                if (fallbackDrive != null)
                {
                    var defaultInstallPath = Path.Combine(fallbackDrive.Name, "Program Files", "WireSock Secure Connect");
                    System.Windows.MessageBox.Show($"Tercih edilen sürücüler bulunamadı. WireSock Secure Connect kurulumu şu konuma yapılacak:\n{defaultInstallPath}", 
                        "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return defaultInstallPath;
                }

                System.Windows.MessageBox.Show("Hiçbir uygun sürücü bulunamadı. Varsayılan konum kullanılacak:\nC:\\Program Files\\WireSock Secure Connect", 
                    "Kurulum Konumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return @"C:\Program Files\WireSock Secure Connect";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Sürücü tespiti sırasında hata oluştu: {ex.Message}\nVarsayılan konum kullanılacak.", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return @"C:\Program Files\WireSock Secure Connect";
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabControl.SelectedIndex == 1) // Gelişmiş Ayarlar sekmesi
            {
                // Sadece dikey boyutu artır, yatay boyut aynı kalsın
                this.Height = 750;
                this.Width = 500;
            }
            else // Ana Sayfa sekmesi
            {
                // Pencere boyutunu normal haline getir
                this.Height = 600;
                this.Width = 500;
            }
        }

        private void BtnByeDPISetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI Split Tunneling Kurulum, WireSock, GoodbyeDPI ve WinDivert hizmetlerini kaldıracak ve DNS adreslerinizi Google DNS için ayarlayacaktır. Ayrıca Windows Packet Filter ve C++ Redistibutable paketini otomatik olarak kuracaktır. Kurulumu başlatmak istediğinizden emin misiniz?", 
                "ByeDPI ST Kurulum", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                PerformByeDPISetup();
            }
        }

        private void BtnByeDPIDLLSetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI DLL Kurulum, GoodbyeDPI, WinDivert, ProxiFyre, wiresock-client-service, ByeDPI hizmetlerini durdurup kaldıracak ve Discord.exe için drover dosyalarını kuracaktır. Kurulumu başlatmak istediğinizden emin misiniz?", 
                "ByeDPI DLL Kurulum", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                PerformByeDPIDLLSetup();
            }
        }

        private async void PerformByeDPISetup()
        {
            ShowLoading(true);
            
            try
            {
                var logPath = GetLogPath();
                File.WriteAllText(logPath, $"ByeDPI ST Kurulum Başlangıç: {DateTime.Now}\n");

                // 1. Prerequisites kurulumları
                File.AppendAllText(logPath, "1. Prerequisites kurulumları başlatılıyor...\n");
                var prereqSuccess = await InstallPrerequisitesAsync();
                
                if (!prereqSuccess)
                {
                    System.Windows.MessageBox.Show("Prerequisites kurulumları başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 2. DNS ayarları
                File.AppendAllText(logPath, "2. DNS ayarları yapılıyor...\n");
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    System.Windows.MessageBox.Show("DNS ayarları başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 3. Hizmetleri kaldır
                File.AppendAllText(logPath, "3. Hizmetler kaldırılıyor...\n");
                var serviceRemovalSuccess = await RemoveServicesAsync();
                
                if (!serviceRemovalSuccess)
                {
                    System.Windows.MessageBox.Show("Hizmet kaldırma işlemi başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 4. ProxiFyre kurulumu
                File.AppendAllText(logPath, "4. ProxiFyre kurulumu yapılıyor...\n");
                var proxifyreSuccess = await InstallProxiFyreAsync();
                
                if (!proxifyreSuccess)
                {
                    System.Windows.MessageBox.Show("ProxiFyre kurulumu başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 5. ProxiFyreService başlat
                File.AppendAllText(logPath, "5. ProxiFyreService başlatılıyor...\n");
                var serviceStartSuccess = await StartProxiFyreServiceAsync();
                
                if (!serviceStartSuccess)
                {
                    // System.Windows.MessageBox.Show("ProxiFyreService başlatılamadı. Manuel olarak başlatmayı deneyin.", 
                       // "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 6. Windows Firewall kuralları ekleme
                File.AppendAllText(logPath, "6. Windows Firewall kuralları ekleniyor...\n");
                var firewallSuccess = await AddFirewallRulesAsync();
                
                if (!firewallSuccess)
                {
                    System.Windows.MessageBox.Show("Windows Firewall kuralları eklenirken hata oluştu. Manuel olarak izin vermeniz gerekebilir.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 7. ByeDPI hizmeti kurulumu
                File.AppendAllText(logPath, "7. ByeDPI hizmeti kurulumu yapılıyor...\n");
                var byeDPIInstallSuccess = await InstallByeDPIServiceAsync();
                
                if (!byeDPIInstallSuccess)
                {
                    System.Windows.MessageBox.Show("ByeDPI hizmeti kurulumu başarısız oldu. Manuel olarak başlatmayı deneyin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 8. Kurulum tamamlandı mesajı
                File.AppendAllText(logPath, "Kurulum tamamlandı.\n");
                File.AppendAllText(logPath, $"ByeDPI ST Kurulum Bitiş: {DateTime.Now}\n");

                var restartResult = System.Windows.MessageBox.Show(
                    "ByeDPI ST Kurulum tamamlandı. Değişikliklerin uygulanabilmesi için sisteminizi yeniden başlatın. Şimdi yeniden başlatmak istiyorsanız Evet'e tıklayın. Daha sonra yeniden başlatmak istiyorsanız Hayır'a tıklayın.",
                    "Kurulum Tamamlandı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (restartResult == MessageBoxResult.Yes)
                {
                    RestartSystem();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ByeDPI ST Kurulum sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async void PerformByeDPIDLLSetup()
        {
            ShowLoading(true);
            
            try
            {
                var logPath = GetLogPath();
                File.WriteAllText(logPath, $"ByeDPI DLL Kurulum Başlangıç: {DateTime.Now}\n");

                // 1. Discord.exe'yi durdur
                File.AppendAllText(logPath, "1. Discord.exe durduruluyor...\n");
                var discordStopSuccess = await StopDiscordProcessAsync();
                
                if (!discordStopSuccess)
                {
                    File.AppendAllText(logPath, "Discord.exe durdurulamadı veya çalışmıyor.\n");
                }

                // 2. Hizmetleri durdur ve kaldır
                File.AppendAllText(logPath, "2. Hizmetler durduruluyor ve kaldırılıyor...\n");
                var serviceRemovalSuccess = await RemoveAllServicesForDLLSetupAsync();
                
                if (!serviceRemovalSuccess)
                {
                    System.Windows.MessageBox.Show("Hizmet kaldırma işlemi başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 3. Prerequisites kurulumları
                File.AppendAllText(logPath, "3. Prerequisites kurulumları başlatılıyor...\n");
                var prereqSuccess = await InstallPrerequisitesAsync();
                
                if (!prereqSuccess)
                {
                    System.Windows.MessageBox.Show("Prerequisites kurulumları başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 4. DNS ayarları
                File.AppendAllText(logPath, "4. DNS ayarları yapılıyor...\n");
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    System.Windows.MessageBox.Show("DNS ayarları başarısız oldu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 5. ByeDPI hizmeti kurulumu
                File.AppendAllText(logPath, "5. ByeDPI hizmeti kurulumu yapılıyor...\n");
                var byeDPIInstallSuccess = await InstallByeDPIServiceAsync();
                
                if (!byeDPIInstallSuccess)
                {
                    System.Windows.MessageBox.Show("ByeDPI hizmeti kurulumu başarısız oldu. Manuel olarak başlatmayı deneyin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 6. Windows Firewall kuralları ekleme (sadece ByeDPI için)
                File.AppendAllText(logPath, "6. Windows Firewall kuralları ekleniyor...\n");
                var firewallSuccess = await AddByeDPIFirewallRulesAsync();
                
                if (!firewallSuccess)
                {
                    System.Windows.MessageBox.Show("Windows Firewall kuralları eklenirken hata oluştu. Manuel olarak izin vermeniz gerekebilir.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 7. Drover dosyalarını kopyala
                File.AppendAllText(logPath, "7. Drover dosyaları kopyalanıyor...\n");
                var droverSuccess = await InstallDroverFilesAsync();
                
                if (!droverSuccess)
                {
                    System.Windows.MessageBox.Show("Drover dosyaları kopyalanamadı. Manuel olarak kopyalamayı deneyin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 8. Kurulum tamamlandı mesajı
                File.AppendAllText(logPath, "Kurulum tamamlandı.\n");
                File.AppendAllText(logPath, $"ByeDPI DLL Kurulum Bitiş: {DateTime.Now}\n");

                var restartResult = System.Windows.MessageBox.Show(
                    "ByeDPI DLL Kurulum tamamlandı. Değişikliklerin uygulanabilmesi için sisteminizi yeniden başlatın. Şimdi yeniden başlatmak istiyorsanız Evet'e tıklayın. Daha sonra yeniden başlatmak istiyorsanız Hayır'a tıklayın.",
                    "Kurulum Tamamlandı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (restartResult == MessageBoxResult.Yes)
                {
                    RestartSystem();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ByeDPI DLL Kurulum sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task<bool> InstallPrerequisitesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    var prerequisitesPath = Path.Combine(Environment.CurrentDirectory, "Prerequisites");
                    
                    // Windows Packet Filter kurulumu
                    var packetFilterPath = Path.Combine(prerequisitesPath, "Windows.Packet.Filter.3.6.1.1.x64.msi");
                    if (File.Exists(packetFilterPath))
                    {
                        File.AppendAllText(logPath, "Windows Packet Filter kurulumu başlatılıyor...\n");
                        var packetFilterResult = ExecuteCommand("msiexec", $"/i \"{packetFilterPath}\" /quiet /norestart");
                        File.AppendAllText(logPath, $"Windows Packet Filter kurulum sonucu: {packetFilterResult}\n");
                    }
                    else
                    {
                        // Eski exe dosyasını da kontrol et
                        var oldPacketFilterPath = Path.Combine(prerequisitesPath, "Windows.Packet.Filter.3.6.1.1.exe");
                        if (File.Exists(oldPacketFilterPath))
                        {
                            File.AppendAllText(logPath, "Windows Packet Filter (eski sürüm) kurulumu başlatılıyor...\n");
                            var packetFilterResult = ExecuteCommand(oldPacketFilterPath, "/S");
                            File.AppendAllText(logPath, $"Windows Packet Filter kurulum sonucu: {packetFilterResult}\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, "Windows Packet Filter dosyası bulunamadı.\n");
                        }
                    }

                    // VC++ Redistributable kurulumu
                    var vcRedistPath = Path.Combine(prerequisitesPath, "VC_redist.x64.exe");
                    if (File.Exists(vcRedistPath))
                    {
                        File.AppendAllText(logPath, "VC++ Redistributable kurulumu başlatılıyor...\n");
                        var vcRedistResult = ExecuteCommand(vcRedistPath, "/quiet /norestart");
                        File.AppendAllText(logPath, $"VC++ Redistributable kurulum sonucu: {vcRedistResult}\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "VC++ Redistributable dosyası bulunamadı.\n");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Prerequisites kurulum hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        private async Task<bool> RemoveServicesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    
                    // Hizmetleri durdur ve kaldır
                    var services = new[] { "GoodbyeDPI", "WinDivert", "wiresock-client-service" };
                    
                    foreach (var service in services)
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti kaldırılıyor...\n");
                        
                        // Hizmeti durdur
                        var stopResult = ExecuteCommand("net", $"stop {service}");
                        File.AppendAllText(logPath, $"{service} durdurma sonucu: {stopResult}\n");
                        
                        // Hizmeti kaldır
                        var removeResult = ExecuteCommand("sc", $"delete {service}");
                        File.AppendAllText(logPath, $"{service} kaldırma sonucu: {removeResult}\n");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Hizmet kaldırma hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        private async Task<bool> InstallProxiFyreAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    var proxifyrePath = Path.Combine(Environment.CurrentDirectory, "res", "proxifyre", "ProxiFyre.exe");
                    
                    if (File.Exists(proxifyrePath))
                    {
                        File.AppendAllText(logPath, "ProxiFyre kurulumu başlatılıyor...\n");
                        var result = ExecuteCommand(proxifyrePath, "install");
                        File.AppendAllText(logPath, $"ProxiFyre kurulum sonucu: {result}\n");
                        
                        // ProxiFyreService'in başlangıç türünü Otomatik olarak ayarla
                        File.AppendAllText(logPath, "ProxiFyreService başlangıç türü Otomatik olarak ayarlanıyor...\n");
                        var configResult = ExecuteCommand("sc", "config ProxiFyreService start= auto ");
                        File.AppendAllText(logPath, $"ProxiFyreService başlangıç türü ayarlama sonucu: {configResult}\n");
                        
                        return true; // Hata mesajlarını kaldırdık, her zaman true döndür
                    }
                    else
                    {
                        File.AppendAllText(logPath, "ProxiFyre.exe dosyası bulunamadı.\n");
                        return true; // Hata mesajlarını kaldırdık
                    }
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"ProxiFyre kurulum hatası: {ex.Message}\n");
                    return true; // Hata mesajlarını kaldırdık
                }
            });
        }

        private async Task<bool> StopAndRemoveServicesAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "ProxiFyreService ve ByeDPI hizmetleri durduruluyor ve kaldırılıyor...\n");
                
                var services = new[] { "ProxiFyreService", "ByeDPI" };
                
                foreach (var service in services)
                {
                    // Hizmeti durdur
                    ExecuteCommand("sc", $"stop {service}");
                    File.AppendAllText(logPath, $"{service} hizmeti durduruldu.\n");
                    
                    // Hizmeti sil
                    ExecuteCommand("sc", $"delete {service}");
                    File.AppendAllText(logPath, $"{service} hizmeti silindi.\n");
                }

                // Windows Firewall kurallarını da temizle
                File.AppendAllText(logPath, "Windows Firewall kuralları temizleniyor...\n");
                await RemoveFirewallRulesAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Hizmet durdurma hatası: {ex.Message}\n");
                return false;
            }
        }

        private async Task<bool> StartProxiFyreServiceAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "ProxiFyreService başlatılıyor...\n");

                    // ProxiFyreService'i başlat
                    var startResult = ExecuteCommandString("net", "start ProxiFyreService");
                    File.AppendAllText(logPath, $"ProxiFyreService başlatma sonucu: {startResult}\n");

                    // Başarı kontrolü - birden fazla başarı göstergesi kontrol et
                    var success = startResult.Contains("başlatıldı") || 
                                 startResult.Contains("started") || 
                                 startResult.Contains("SUCCESS") ||
                                 startResult.Contains("service is already running") ||
                                 startResult.Contains("hizmet zaten çalışıyor");

                    // Hizmetin gerçekten çalışıp çalışmadığını kontrol et
                    if (success)
                    {
                        var queryResult = ExecuteCommandString("sc", "query ProxiFyreService");
                        File.AppendAllText(logPath, $"ProxiFyreService durum kontrolü: {queryResult}\n");
                        
                        // Hizmet durumunu kontrol et
                        success = queryResult.Contains("RUNNING") || queryResult.Contains("ÇALIŞIYOR");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"ProxiFyreService başlatma hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        private string GetLogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "byedpi_setup.log");
        }

        private string GetDNSLogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "dns_debug.log");
        }

        private async Task<bool> InstallByeDPIServiceAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "ByeDPI hizmeti kuruluyor...\n");

                    // SplitWire-Turkey.exe'nin çalıştırıldığı klasörü al
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var exeDirectory = Path.GetDirectoryName(exePath);
                    
                    // service_install.bat dosya yolu
                    var serviceInstallPath = Path.Combine(exeDirectory, "res", "byedpi", "service_install.bat");
                    
                    if (!File.Exists(serviceInstallPath))
                    {
                        File.AppendAllText(logPath, $"service_install.bat bulunamadı: {serviceInstallPath}\n");
                        return false;
                    }

                    File.AppendAllText(logPath, $"service_install.bat bulundu: {serviceInstallPath}\n");

                    // service_install.bat dosyasını sessizce çalıştır
                    File.AppendAllText(logPath, "ByeDPI hizmeti service_install.bat ile kuruluyor...\n");
                    var installResult = ExecuteCommand("cmd", $"/c \"{serviceInstallPath}\"");
                    File.AppendAllText(logPath, $"Hizmet kurulum sonucu (Exit Code): {installResult}\n");

                    // Hizmetin başarıyla kurulup kurulmadığını kontrol et
                    var queryResult = ExecuteCommandString("sc", "query ByeDPI");
                    File.AppendAllText(logPath, $"ByeDPI hizmeti durum kontrolü: {queryResult}\n");
                    
                    var installSuccess = queryResult.Contains("RUNNING") || queryResult.Contains("ÇALIŞIYOR") || 
                                       queryResult.Contains("STOPPED") || queryResult.Contains("DURDURULDU");

                    if (installSuccess)
                    {
                        File.AppendAllText(logPath, "ByeDPI hizmeti başarıyla kuruldu.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "ByeDPI hizmeti kurulamadı.\n");
                    }

                    return installSuccess;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"ByeDPI hizmeti kurulum hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        private int ExecuteCommand(string command, string arguments)
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Komut çalıştırılıyor: {command} {arguments}\n");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Yönetici izni ile çalıştır
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                File.AppendAllText(logPath, $"Çıktı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"Hata: {error}\n");
                }
                File.AppendAllText(logPath, $"Exit Code: {process.ExitCode}\n");
                
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Komut Hatası: {ex.Message}\n");
                return -1;
            }
        }

        private string ExecuteCommandString(string command, string arguments)
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Komut çalıştırılıyor: {command} {arguments}\n");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Yönetici izni ile çalıştır
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                File.AppendAllText(logPath, $"Çıktı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"Hata: {error}\n");
                }
                File.AppendAllText(logPath, $"Exit Code: {process.ExitCode}\n");
                
                return output + error; // Hem çıktı hem hata mesajını döndür
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Komut Hatası: {ex.Message}\n");
                return $"Hata: {ex.Message}";
            }
        }

        private async Task<bool> SetModernDNSSettingsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool allCommandsSuccessful = true;
                    var logPath = GetDNSLogPath();
                    File.WriteAllText(logPath, $"DNS Ayarı Başlangıç: {DateTime.Now}\n");

                    // PowerShell script ile DNS ayarlarını yap
                    Debug.WriteLine("PowerShell ile DNS ayarları yapılıyor...");
                    File.AppendAllText(logPath, "PowerShell ile DNS ayarları yapılıyor...\n");

                    var psScript = @"
$i = Get-NetAdapter -Physical
$i | Get-DnsClientServerAddress -AddressFamily IPv4 | Set-DnsClientServerAddress -ServerAddresses '8.8.8.8', '9.9.9.9'
$i | Get-DnsClientServerAddress -AddressFamily IPv6 | Set-DnsClientServerAddress -ServerAddresses '2001:4860:4860::8888', '2620:fe::9'

# DoH şablonlarını temizle ve yeniden ayarla
$i | ForEach-Object {
    $adapterGuid = $_.InterfaceGuid
    
    # Mevcut DoH ayarlarını temizle
    $dohPath = 'HKLM:System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\' + $adapterGuid + '\DohInterfaceSettings'
    if (Test-Path $dohPath) {
        Remove-Item -Path $dohPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    # Google DNS (8.8.8.8) için DoH ayarı
    $googlePath = $dohPath + '\Doh\8.8.8.8'
    New-Item -Path $googlePath -Force | Out-Null
    New-ItemProperty -Path $googlePath -Name 'DohFlags' -Value 1 -PropertyType Qword | Out-Null
    New-ItemProperty -Path $googlePath -Name 'DohTemplate' -Value 'https://dns.google/dns-query' -PropertyType String | Out-Null
    
    # Quad9 DNS (9.9.9.9) için DoH ayarı
    $quad9Path = $dohPath + '\Doh\9.9.9.9'
    New-Item -Path $quad9Path -Force | Out-Null
    New-ItemProperty -Path $quad9Path -Name 'DohFlags' -Value 1 -PropertyType Qword | Out-Null
    New-ItemProperty -Path $quad9Path -Name 'DohTemplate' -Value 'https://dns.quad9.net/dns-query' -PropertyType String | Out-Null
    
    # Google DNS IPv6 (2001:4860:4860::8888) için DoH ayarı
    $googleIPv6Path = $dohPath + '\Doh6\2001:4860:4860::8888'
    New-Item -Path $googleIPv6Path -Force | Out-Null
    New-ItemProperty -Path $googleIPv6Path -Name 'DohFlags' -Value 1 -PropertyType Qword | Out-Null
    New-ItemProperty -Path $googleIPv6Path -Name 'DohTemplate' -Value 'https://dns.google/dns-query' -PropertyType String | Out-Null
    
    # Quad9 DNS IPv6 (2620:fe::9) için DoH ayarı
    $quad9IPv6Path = $dohPath + '\Doh6\2620:fe::9'
    New-Item -Path $quad9IPv6Path -Force | Out-Null
    New-ItemProperty -Path $quad9IPv6Path -Name 'DohFlags' -Value 1 -PropertyType Qword | Out-Null
    New-ItemProperty -Path $quad9IPv6Path -Name 'DohTemplate' -Value 'https://dns.quad9.net/dns-query' -PropertyType String | Out-Null
}

Clear-DnsClientCache;
";

                    var result = ExecutePowerShellScript(psScript);
                    
                    if (result == 0)
                    {
                        Debug.WriteLine("PowerShell DNS ayarları başarılı.");
                        File.AppendAllText(logPath, "PowerShell DNS ayarları başarılı.\n");
                        allCommandsSuccessful = true;
                    }
                    else
                    {
                        Debug.WriteLine($"PowerShell DNS ayarları başarısız. Exit Code: {result}");
                        File.AppendAllText(logPath, $"PowerShell DNS ayarları başarısız. Exit Code: {result}\n");
                        allCommandsSuccessful = false;
                    }

                    // DNS ayarlarını doğrula
                    File.AppendAllText(logPath, "DNS ayarlarını doğrulama...\n");
                    var verificationResult = VerifyDNSSettings();
                    if (verificationResult)
                    {
                        Debug.WriteLine("DNS ayarları doğrulandı.");
                        File.AppendAllText(logPath, "DNS ayarları doğrulandı.\n");
                    }
                    else
                    {
                        Debug.WriteLine("DNS ayarları doğrulanamadı.");
                        File.AppendAllText(logPath, "DNS ayarları doğrulanamadı.\n");
                        allCommandsSuccessful = false;
                    }

                    File.AppendAllText(logPath, $"Genel sonuç: {allCommandsSuccessful}\n");
                    File.AppendAllText(logPath, $"DNS Ayarı Bitiş: {DateTime.Now}\n");
                    
                    return allCommandsSuccessful;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Modern DNS ayar hatası: {ex.Message}");
                    var logPath = GetDNSLogPath();
                    File.AppendAllText(logPath, $"HATA: {ex.Message}\n");
                    return false;
                }
            });
        }

        private int ExecutePowerShellScript(string script)
        {
            try
            {
                var logPath = GetDNSLogPath();
                File.AppendAllText(logPath, $"PowerShell Script çalıştırılıyor...\n");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Yönetici izni ile çalıştır
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                Debug.WriteLine($"PowerShell çıktısı: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"PowerShell hatası: {error}");
                }
                
                File.AppendAllText(logPath, $"PowerShell Çıktısı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"PowerShell Hatası: {error}\n");
                }
                File.AppendAllText(logPath, $"PowerShell Exit Code: {process.ExitCode}\n");
                
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PowerShell execution failed: {ex.Message}");
                var logPath = GetDNSLogPath();
                File.AppendAllText(logPath, $"PowerShell Hatası: {ex.Message}\n");
                return -1;
            }
        }

                private bool VerifyDNSSettings()
        {
            try
            {
                var logPath = GetDNSLogPath();
                
                // PowerShell ile DNS ayarlarını kontrol et
                var checkScript = @"
$adapters = Get-NetAdapter -Physical
$results = @()
foreach($adapter in $adapters) {
    $ipv4 = $adapter | Get-DnsClientServerAddress -AddressFamily IPv4
    $ipv6 = $adapter | Get-DnsClientServerAddress -AddressFamily IPv6
    $result = [PSCustomObject]@{
        AdapterName = $adapter.Name
        IPv4Servers = $ipv4.ServerAddresses -join ','
        IPv6Servers = $ipv6.ServerAddresses -join ','
    }
    $results += $result
}
$results | ConvertTo-Json
";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-ExecutionPolicy Bypass -Command \"{checkScript}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                Debug.WriteLine($"DNS ayarları kontrolü:\n{output}");
                File.AppendAllText(logPath, $"DNS Ayarları Kontrolü:\n{output}\n");

                // DoH ayarlarını kontrol et
                var dohCheckScript = @"
$adapters = Get-NetAdapter -Physical
$dohResults = @()
foreach($adapter in $adapters) {
    $dohPath = 'HKLM:System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\' + $adapter.InterfaceGuid + '\DohInterfaceSettings'
    $dohSettings = Get-ChildItem -Path $dohPath -Recurse -ErrorAction SilentlyContinue
    $result = [PSCustomObject]@{
        AdapterName = $adapter.Name
        DoHSettings = $dohSettings.Count
    }
    $dohResults += $result
    }
$dohResults | ConvertTo-Json
";

                var dohProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-ExecutionPolicy Bypass -Command \"{dohCheckScript}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                dohProcess.Start();
                var dohOutput = dohProcess.StandardOutput.ReadToEnd();
                dohProcess.WaitForExit();

                Debug.WriteLine($"DoH ayarları kontrolü:\n{dohOutput}");
                File.AppendAllText(logPath, $"DoH Ayarları Kontrolü:\n{dohOutput}\n");

                // Kontrol sonuçları
                bool hasCorrectDNS = output.Contains("8.8.8.8") && output.Contains("9.9.9.9");
                bool hasDoHSettings = dohOutput.Contains("DoHSettings") && !dohOutput.Contains("0");

                Debug.WriteLine($"Doğru DNS: {hasCorrectDNS}, DoH Ayarları: {hasDoHSettings}");
                File.AppendAllText(logPath, $"Doğrulama Sonuçları: Doğru DNS={hasCorrectDNS}, DoH Ayarları={hasDoHSettings}\n");

                return hasCorrectDNS && hasDoHSettings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DNS doğrulama hatası: {ex.Message}");
                var logPath = GetDNSLogPath();
                File.AppendAllText(logPath, $"Doğrulama Hatası: {ex.Message}\n");
                return false;
            }
        }

        /// <summary>
        /// Windows Firewall kuralları ekler
        /// </summary>
        private async Task<bool> AddFirewallRulesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "Windows Firewall kuralları ekleniyor...\n");

                    // SplitWire-Turkey.exe'nin çalıştırıldığı klasörü al
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var exeDirectory = Path.GetDirectoryName(exePath);

                    // ProxiFyre.exe yolu
                    var proxifyrePath = Path.Combine(exeDirectory, "res", "proxifyre", "ProxiFyre.exe");
                    
                    // ciadpi.exe yolu
                    var ciadpiPath = Path.Combine(exeDirectory, "res", "byedpi", "ciadpi.exe");

                    bool allRulesAdded = true;

                    // ProxiFyre.exe için firewall kuralı ekle
                    if (File.Exists(proxifyrePath))
                    {
                        File.AppendAllText(logPath, $"ProxiFyre.exe firewall kuralı ekleniyor: {proxifyrePath}\n");
                        
                        // Gelen bağlantılar için kural
                        var inboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ProxiFyre Inbound\" dir=in action=allow program=\"{proxifyrePath}\" enable=yes");
                        File.AppendAllText(logPath, $"ProxiFyre Inbound kural sonucu: {inboundResult}\n");
                        
                        // Giden bağlantılar için kural
                        var outboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ProxiFyre Outbound\" dir=out action=allow program=\"{proxifyrePath}\" enable=yes");
                        File.AppendAllText(logPath, $"ProxiFyre Outbound kural sonucu: {outboundResult}\n");

                        if (inboundResult != 0 || outboundResult != 0)
                        {
                            File.AppendAllText(logPath, "ProxiFyre firewall kuralları eklenirken hata oluştu.\n");
                            allRulesAdded = false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"ProxiFyre.exe bulunamadı: {proxifyrePath}\n");
                        allRulesAdded = false;
                    }

                    // ciadpi.exe için firewall kuralı ekle
                    if (File.Exists(ciadpiPath))
                    {
                        File.AppendAllText(logPath, $"ciadpi.exe firewall kuralı ekleniyor: {ciadpiPath}\n");
                        
                        // Gelen bağlantılar için kural
                        var inboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ByeDPI ciadpi Inbound\" dir=in action=allow program=\"{ciadpiPath}\" enable=yes");
                        File.AppendAllText(logPath, $"ciadpi Inbound kural sonucu: {inboundResult}\n");
                        
                        // Giden bağlantılar için kural
                        var outboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ByeDPI ciadpi Outbound\" dir=out action=allow program=\"{ciadpiPath}\" enable=yes");
                        File.AppendAllText(logPath, $"ciadpi Outbound kural sonucu: {outboundResult}\n");

                        if (inboundResult != 0 || outboundResult != 0)
                        {
                            File.AppendAllText(logPath, "ciadpi firewall kuralları eklenirken hata oluştu.\n");
                            allRulesAdded = false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"ciadpi.exe bulunamadı: {ciadpiPath}\n");
                        allRulesAdded = false;
                    }

                    if (allRulesAdded)
                    {
                        File.AppendAllText(logPath, "Tüm Windows Firewall kuralları başarıyla eklendi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Bazı Windows Firewall kuralları eklenirken hata oluştu.\n");
                    }

                    return allRulesAdded;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Firewall kural ekleme hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        /// <summary>
        /// Drover dosyalarını temizler
        /// </summary>
        private async Task<bool> CleanupDroverFilesAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "Drover dosyaları temizleniyor...\n");

                // Önce Discord.exe'yi durdur
                File.AppendAllText(logPath, "Discord.exe durduruluyor...\n");
                var discordProcesses = Process.GetProcessesByName("Discord");
                if (discordProcesses.Length > 0)
                {
                    foreach (var process in discordProcesses)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(5000); // 5 saniye bekle
                            File.AppendAllText(logPath, $"Discord.exe işlemi durduruldu. PID: {process.Id}\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(logPath, $"Discord.exe işlemi durdurulurken hata: {ex.Message}\n");
                        }
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "Discord.exe işlemi çalışmıyor.\n");
                }

                // Kısa bir bekleme süresi ekle
                Thread.Sleep(2000);

                // Discord.exe'nin bulunduğu klasörü bul (timeout ile)
                var discordPath = await FindDiscordPathWithTimeoutAsync();
                if (!string.IsNullOrEmpty(discordPath))
                {
                    var versionDllPath = Path.Combine(discordPath, "version.dll");
                    var droverIniPath = Path.Combine(discordPath, "drover.ini");

                    // Dosyaları sil
                    if (File.Exists(versionDllPath))
                    {
                        try
                        {
                            File.Delete(versionDllPath);
                            File.AppendAllText(logPath, $"version.dll silindi: {versionDllPath}\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(logPath, $"version.dll silinirken hata: {ex.Message}\n");
                        }
                    }

                    if (File.Exists(droverIniPath))
                    {
                        try
                        {
                            File.Delete(droverIniPath);
                            File.AppendAllText(logPath, $"drover.ini silindi: {droverIniPath}\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(logPath, $"drover.ini silinirken hata: {ex.Message}\n");
                        }
                    }

                    File.AppendAllText(logPath, "Drover dosyaları başarıyla temizlendi.\n");
                    return true;
                }
                else
                {
                    File.AppendAllText(logPath, "Discord.exe bulunamadı, drover dosyaları temizlenmedi.\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Drover dosyaları temizleme hatası: {ex.Message}\n");
                return false;
            }
        }

        /// <summary>
        /// Discord.exe'nin bulunduğu klasörü bulur (timeout ile)
        /// </summary>
        private async Task<string> FindDiscordPathWithTimeoutAsync()
        {
            try
            {
                // 10 saniye timeout ile Discord yolu bulma işlemini çalıştır
                var timeoutTask = Task.Run(() => FindDiscordPath());
                var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(10000)); // 10 saniye timeout
                
                if (completedTask == timeoutTask)
                {
                    return await timeoutTask; // Timeout olmadan tamamlandı
                }
                else
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "Discord yolu bulma işlemi timeout nedeniyle iptal edildi.\n");
                    return null; // Timeout oldu
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Discord yolu bulma timeout hatası: {ex.Message}\n");
                return null;
            }
        }

        /// <summary>
        /// Discord.exe'nin bulunduğu klasörü bulur
        /// </summary>
        private string FindDiscordPath()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "Discord.exe yolu aranıyor...\n");

                // Önce Discord.exe'yi çalışan işlemler arasında ara
                var discordProcesses = Process.GetProcessesByName("Discord");
                if (discordProcesses.Length > 0)
                {
                    var process = discordProcesses[0];
                    var processPath = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(processPath))
                    {
                        var directory = Path.GetDirectoryName(processPath);
                        File.AppendAllText(logPath, $"Discord.exe çalışan işlemden bulundu: {directory}\n");
                        return directory;
                    }
                }

                // LocalAppData klasöründe Discord klasörünü ara
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var discordBasePath = Path.Combine(localAppData, "Discord");

                if (Directory.Exists(discordBasePath))
                {
                    File.AppendAllText(logPath, $"Discord base path bulundu: {discordBasePath}\n");
                    
                    // app-* klasörlerini ara
                    var appDirectories = Directory.GetDirectories(discordBasePath, "app-*");
                    File.AppendAllText(logPath, $"{appDirectories.Length} adet app-* klasörü bulundu.\n");
                    
                    foreach (var appDir in appDirectories)
                    {
                        var discordExePath = Path.Combine(appDir, "Discord.exe");
                        if (File.Exists(discordExePath))
                        {
                            File.AppendAllText(logPath, $"Discord.exe app klasöründe bulundu: {appDir}\n");
                            return appDir;
                        }
                    }

                    // Eğer app-* klasörü bulunamazsa, Discord klasörünün kendisini kontrol et
                    var discordExeInBase = Path.Combine(discordBasePath, "Discord.exe");
                    if (File.Exists(discordExeInBase))
                    {
                        File.AppendAllText(logPath, $"Discord.exe base klasörde bulundu: {discordBasePath}\n");
                        return discordBasePath;
                    }
                }

                File.AppendAllText(logPath, "Discord.exe otomatik olarak bulunamadı.\n");
                return null;
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Discord yolu bulma hatası: {ex.Message}\n");
                return null;
            }
        }

        /// <summary>
        /// Discord.exe işlemini durdurur
        /// </summary>
        private async Task<bool> StopDiscordProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "Discord.exe işlemi durduruluyor...\n");

                    var discordProcesses = Process.GetProcessesByName("Discord");
                    if (discordProcesses.Length > 0)
                    {
                        foreach (var process in discordProcesses)
                        {
                            try
                            {
                                process.Kill();
                                process.WaitForExit(5000); // 5 saniye bekle
                                File.AppendAllText(logPath, $"Discord.exe işlemi durduruldu. PID: {process.Id}\n");
                            }
                            catch (Exception ex)
                            {
                                File.AppendAllText(logPath, $"Discord.exe işlemi durdurulurken hata: {ex.Message}\n");
                            }
                        }
                        return true;
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Discord.exe işlemi çalışmıyor.\n");
                        return true; // Çalışmıyorsa başarılı say
                    }
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Discord işlemi durdurma hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        /// <summary>
        /// ByeDPI DLL kurulumu için tüm hizmetleri kaldırır
        /// </summary>
        private async Task<bool> RemoveAllServicesForDLLSetupAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "ByeDPI DLL kurulumu için hizmetler kaldırılıyor...\n");
                    
                    var services = new[] { "GoodbyeDPI", "WinDivert", "ProxiFyre", "wiresock-client-service", "ByeDPI" };
                    
                    foreach (var service in services)
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti kaldırılıyor...\n");
                        
                        // Hizmeti durdur
                        var stopResult = ExecuteCommand("net", $"stop {service}");
                        File.AppendAllText(logPath, $"{service} durdurma sonucu: {stopResult}\n");
                        
                        // Hizmeti kaldır
                        var removeResult = ExecuteCommand("sc", $"delete {service}");
                        File.AppendAllText(logPath, $"{service} kaldırma sonucu: {removeResult}\n");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Hizmet kaldırma hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        /// <summary>
        /// ByeDPI için firewall kuralları ekler
        /// </summary>
        private async Task<bool> AddByeDPIFirewallRulesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "ByeDPI için Windows Firewall kuralları ekleniyor...\n");

                    // SplitWire-Turkey.exe'nin çalıştırıldığı klasörü al
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var exeDirectory = Path.GetDirectoryName(exePath);
                    
                    // ciadpi.exe yolu
                    var ciadpiPath = Path.Combine(exeDirectory, "res", "byedpi", "ciadpi.exe");

                    bool allRulesAdded = true;

                    // ciadpi.exe için firewall kuralı ekle
                    if (File.Exists(ciadpiPath))
                    {
                        File.AppendAllText(logPath, $"ciadpi.exe firewall kuralı ekleniyor: {ciadpiPath}\n");
                        
                        // Gelen bağlantılar için kural
                        var inboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ByeDPI ciadpi Inbound\" dir=in action=allow program=\"{ciadpiPath}\" enable=yes");
                        File.AppendAllText(logPath, $"ciadpi Inbound kural sonucu: {inboundResult}\n");
                        
                        // Giden bağlantılar için kural
                        var outboundResult = ExecuteCommand("netsh", $"advfirewall firewall add rule name=\"ByeDPI ciadpi Outbound\" dir=out action=allow program=\"{ciadpiPath}\" enable=yes");
                        File.AppendAllText(logPath, $"ciadpi Outbound kural sonucu: {outboundResult}\n");

                        if (inboundResult != 0 || outboundResult != 0)
                        {
                            File.AppendAllText(logPath, "ciadpi firewall kuralları eklenirken hata oluştu.\n");
                            allRulesAdded = false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"ciadpi.exe bulunamadı: {ciadpiPath}\n");
                        allRulesAdded = false;
                    }

                    if (allRulesAdded)
                    {
                        File.AppendAllText(logPath, "ByeDPI Windows Firewall kuralları başarıyla eklendi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "ByeDPI Windows Firewall kuralları eklenirken hata oluştu.\n");
                    }

                    return allRulesAdded;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"ByeDPI firewall kural ekleme hatası: {ex.Message}\n");
                    return false;
                }
            });
        }

        /// <summary>
        /// Drover dosyalarını Discord klasörüne kopyalar
        /// </summary>
        private async Task<bool> InstallDroverFilesAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "Drover dosyaları kopyalanıyor...\n");

                // Discord.exe'nin bulunduğu klasörü bul (timeout ile)
                var discordPath = await FindDiscordPathWithTimeoutAsync();
                    if (string.IsNullOrEmpty(discordPath))
                    {
                        // Discord.exe bulunamadı, manuel seçim için dialog aç
                        File.AppendAllText(logPath, "Discord.exe bulunamadı, manuel seçim için dialog açılıyor...\n");
                        
                        var result = System.Windows.MessageBox.Show(
                            "Discord.exe otomatik olarak bulunamadı. Manuel olarak Discord klasörünü seçmek ister misiniz?",
                            "Discord Klasörü Bulunamadı",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            var dialog = new FolderBrowserDialog
                            {
                                Description = "Discord.exe'nin bulunduğu klasörü seçin"
                            };

                            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                            {
                                discordPath = dialog.SelectedPath;
                                File.AppendAllText(logPath, $"Manuel seçilen Discord yolu: {discordPath}\n");
                            }
                            else
                            {
                                File.AppendAllText(logPath, "Manuel seçim iptal edildi.\n");
                                return false;
                            }
                        }
                        else
                        {
                            File.AppendAllText(logPath, "Manuel seçim reddedildi.\n");
                            return false;
                        }
                    }

                    // Drover dosyalarının kaynak yolları
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var exeDirectory = Path.GetDirectoryName(exePath);
                    var droverSourcePath = Path.Combine(exeDirectory, "res", "drover");
                    
                    var versionDllSource = Path.Combine(droverSourcePath, "version.dll");
                    var droverIniSource = Path.Combine(droverSourcePath, "drover.ini");
                    
                    // Hedef yolları
                    var versionDllTarget = Path.Combine(discordPath, "version.dll");
                    var droverIniTarget = Path.Combine(discordPath, "drover.ini");

                    bool allFilesCopied = true;

                    // version.dll kopyala
                    if (File.Exists(versionDllSource))
                    {
                        try
                        {
                            File.Copy(versionDllSource, versionDllTarget, true);
                            File.AppendAllText(logPath, $"version.dll kopyalandı: {versionDllTarget}\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(logPath, $"version.dll kopyalama hatası: {ex.Message}\n");
                            allFilesCopied = false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"version.dll kaynak dosyası bulunamadı: {versionDllSource}\n");
                        allFilesCopied = false;
                    }

                    // drover.ini kopyala
                    if (File.Exists(droverIniSource))
                    {
                        try
                        {
                            File.Copy(droverIniSource, droverIniTarget, true);
                            File.AppendAllText(logPath, $"drover.ini kopyalandı: {droverIniTarget}\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(logPath, $"drover.ini kopyalama hatası: {ex.Message}\n");
                            allFilesCopied = false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"drover.ini kaynak dosyası bulunamadı: {droverIniSource}\n");
                        allFilesCopied = false;
                    }

                    if (allFilesCopied)
                    {
                        File.AppendAllText(logPath, "Drover dosyaları başarıyla kopyalandı.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Bazı drover dosyaları kopyalanamadı.\n");
                    }

                    return allFilesCopied;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Drover dosyaları kopyalama hatası: {ex.Message}\n");
                    return false;
                }
        }

        /// <summary>
        /// Mevcut firewall kurallarını temizler
        /// </summary>
        private async Task<bool> RemoveFirewallRulesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, "Mevcut Windows Firewall kuralları temizleniyor...\n");

                    // ProxiFyre kurallarını kaldır
                    var proxifyreInboundResult = ExecuteCommand("netsh", "advfirewall firewall delete rule name=\"ProxiFyre Inbound\"");
                    var proxifyreOutboundResult = ExecuteCommand("netsh", "advfirewall firewall delete rule name=\"ProxiFyre Outbound\"");
                    
                    File.AppendAllText(logPath, $"ProxiFyre kuralları kaldırma sonucu: Inbound={proxifyreInboundResult}, Outbound={proxifyreOutboundResult}\n");

                    // ciadpi kurallarını kaldır
                    var ciadpiInboundResult = ExecuteCommand("netsh", "advfirewall firewall delete rule name=\"ByeDPI ciadpi Inbound\"");
                    var ciadpiOutboundResult = ExecuteCommand("netsh", "advfirewall firewall delete rule name=\"ByeDPI ciadpi Outbound\"");
                    
                    File.AppendAllText(logPath, $"ciadpi kuralları kaldırma sonucu: Inbound={ciadpiInboundResult}, Outbound={ciadpiOutboundResult}\n");

                    File.AppendAllText(logPath, "Windows Firewall kuralları temizlendi.\n");
                    return true;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Firewall kural temizleme hatası: {ex.Message}\n");
                    return false;
                }
            });
        }
    }
} 