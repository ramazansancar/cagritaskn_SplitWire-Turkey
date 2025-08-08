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

        private async Task UpdateWireSockStatusAsync()
        {
            try
            {
                var isInstalled = await _wireSockService.IsWireSockInstalledAsync();
                
                await Dispatcher.InvokeAsync(() =>
                {
                    if (isInstalled)
                    {
                        txtWiresockStatus.Text = "";
                        txtWiresockStatus.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        txtWiresockStatus.Text = "WireSock yüklü değil!";
                        txtWiresockStatus.Foreground = System.Windows.Media.Brushes.Red;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateWireSockStatusAsync hatası: {ex.Message}");
                // Hata durumunda varsayılan durumu göster
                await Dispatcher.InvokeAsync(() =>
                {
                    txtWiresockStatus.Text = "Durum kontrol ediliyor...";
                    txtWiresockStatus.Foreground = System.Windows.Media.Brushes.Orange;
                });
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Hızlı kurulum başlatmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            // Standart kurulum log dosyasını başlat
            var standardLogPath = GetStandardSetupLogPath();
            File.WriteAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM BAŞLATILIYOR ===\n");
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1. AŞAMA: Hizmet temizliği başlatılıyor...\n");
                
                // Önce ProxiFyreService ve ByeDPI hizmetlerini durdur ve kaldır
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.1. ProxiFyreService ve ByeDPI hizmetleri kaldırılıyor...\n");
                await StopAndRemoveServicesAsync();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.1. ProxiFyreService ve ByeDPI hizmetleri kaldırma tamamlandı.\n");
                
                // Önce tüm hizmetleri kaldır
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.2. Tüm hizmetler kaldırılıyor...\n");
                await RemoveAllServicesAsync();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.2. Tüm hizmetler kaldırma tamamlandı.\n");
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1. AŞAMA: Hizmet temizliği tamamlandı.\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2. AŞAMA: WireSock kurulum kontrolü başlatılıyor...\n");
                
                if (!_wireSockService.IsLatestWireSockInstalled())
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.1. WireSock son sürümü yüklü değil. Kurulum gerekli.\n");
                    
                    var downloadResult = System.Windows.MessageBox.Show("WireSock'un son sürümü yüklü değil. WireSock kurulum dosyasını çalıştırmak ister misiniz?", 
                        "WireSock Son Sürüm Yüklü Değil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (downloadResult == MessageBoxResult.Yes)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2. WireSock kurulumu başlatılıyor...\n");
                        await DownloadAndInstallWireSock();
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2. WireSock kurulumu tamamlandı.\n");
                        
                        // Kurulum sonrası tekrar kontrol et
                        if (!_wireSockService.IsLatestWireSockInstalled())
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.3. UYARI: WireSock kurulumu tamamlandı ancak kurulum doğrulanamadı.\n");
                            System.Windows.MessageBox.Show("WireSock kurulumu tamamlandı ancak kurulum doğrulanamadı. Kuruluma devam ediliyor...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.3. WireSock kurulumu başarıyla doğrulandı.\n");
                        }
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2. Kullanıcı WireSock kurulumunu iptal etti.\n");
                        ShowLoading(false);
                        return;
                    }
                }
                else
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.1. WireSock son sürümü zaten yüklü.\n");
                }
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2. AŞAMA: WireSock kurulum kontrolü tamamlandı.\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. AŞAMA: Hızlı kurulum başlatılıyor...\n");
                await PerformFastSetup();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. AŞAMA: Hızlı kurulum tamamlandı.\n");
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM BAŞARIYLA TAMAMLANDI ===\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Standart kurulum sırasında hata oluştu: {ex.Message}\n");
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM HATA İLE SONLANDI ===\n");
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

        private void BtnRecepBaltas_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Yazılımın geliştirilmesine katkıda bulunan Techolay.net kurucusu Recep Baltaş'a çok teşekkür ederim. Techolay.net Sosyal'i ziyaret etmek için Evet butonuna basın.",
                "Recep Baltaş", MessageBoxButton.YesNo, MessageBoxImage.Information);
            
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://techolay.net/sosyal/",
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
                    var downloadResult = System.Windows.MessageBox.Show("WireSock yüklü değil. WireSock kurulum dosyasını çalıştırmak ister misiniz?", 
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
            var standardLogPath = GetStandardSetupLogPath();
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.1. Drover dosyaları temizleniyor...\n");
                // Drover dosyalarını temizle
                await CleanupDroverFilesAsync();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.1. Drover dosyaları temizleme tamamlandı.\n");
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2. DNS ayarları yapılıyor...\n");
                // DNS ayarları
                var dnsSuccess = await SetModernDNSSettingsAsync();
                
                if (!dnsSuccess)
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2. UYARI: DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }
                else
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2. DNS ayarları başarıyla yapıldı.\n");
                }

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.3. WireGuard profili oluşturuluyor...\n");
                var success = await _wireGuardService.CreateProfileAsync();
                
                if (success)
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.3. WireGuard profili başarıyla oluşturuldu.\n");
                    var configPath = _wireGuardService.GetConfigPath();
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.3. Konfigürasyon dosyası yolu: {configPath}\n");
                    
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.4. WireSock hizmeti kuruluyor...\n");
                    var serviceResult = await _wireSockService.InstallServiceAsync(configPath);
                    
                    if (serviceResult)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.4. WireSock hizmeti başarıyla kuruldu.\n");
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.5. Sistem yeniden başlatma mesajı gösteriliyor.\n");
                        ShowRestartMessage();
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.5. Sistem yeniden başlatma mesajı gösterildi.\n");
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.4. UYARI: WireSock hizmeti kurulamadı.\n");
                    }
                }
                else
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.3. HATA: WireGuard profili oluşturulamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. HATA: Hızlı kurulum sırasında hata oluştu: {ex.Message}\n");
            }
            finally
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. AŞAMA: Hızlı kurulum tamamlandı.\n");
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
                else
                {
                    File.AppendAllText(logPath, "DNS ayarları başarıyla yapıldı.\n");
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
                else
                {
                    File.AppendAllText(logPath, "DNS ayarları başarıyla yapıldı.\n");
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

                // Mevcut WireSock kurulumunu kaldır - /res klasöründeki dosyayı kullan
                File.AppendAllText(logPath, "Mevcut WireSock kurulumu kaldırılıyor...\n");
                
                var localUninstallPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "wiresock-secure-connect-x64-2.4.16.1.exe");
                
                if (File.Exists(localUninstallPath))
                {
                    try
                    {
                        // Mevcut sürümü kaldır
                        File.AppendAllText(logPath, "Mevcut WireSock sürümü kaldırılıyor...\n");
                        var uninstallProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = localUninstallPath,
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
                }
                else
                {
                    File.AppendAllText(logPath, $"WireSock kaldırma dosyası bulunamadı: {localUninstallPath}\n");
                }

                // 1.4.7.1 sürümünü indir ve kur
                File.AppendAllText(logPath, "WireSock 1.4.7.1 sürümü indiriliyor...\n");
                var tempDir = Path.Combine(Path.GetTempPath(), "SplitWireTurkey");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);
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
                            // Check if WireSock is actually installed despite the error
                            if (_wireSockService.IsWireSockInstalled())
                            {
                                File.AppendAllText(logPath, $"MSI kurulum başarılı (Exit Code: {process.ExitCode}).\n");
                                System.Windows.MessageBox.Show($"WireSock 1.4.7.1 sürümü kurulumu tamamlandı (Exit Code: {process.ExitCode}).\nKuruluma devam ediliyor ...", 
                                    "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                                msiInstallSuccess = true;
                                break;
                            }
                            else
                            {
                                File.AppendAllText(logPath, $"MSI kurulum başarısız. Exit Code: {process.ExitCode}\n");
                            }
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
                    
                    // Check if WireSock is actually installed
                    if (_wireSockService.IsWireSockInstalled())
                    {
                        System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kurulumu tamamlandı. Kuruluma devam ediliyor ...", 
                            "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kurulumu tamamlandı ancak kurulum doğrulanamadı. Kuruluma devam ediliyor ...", 
                            "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                
                await UpdateWireSockStatusAsync();
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
            var standardLogPath = GetStandardSetupLogPath();
            
            try
            {
                ShowLoading(true);
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.1. WireSock kurulum dosyası kontrol ediliyor...\n");
                
                // Doğrudan /res klasöründeki dosyayı kullan
                var localSetupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "wiresock-secure-connect-x64-2.4.16.1.exe");
                
                if (!File.Exists(localSetupPath))
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.1. HATA: WireSock kurulum dosyası bulunamadı: {localSetupPath}\n");
                    System.Windows.MessageBox.Show($"WireSock kurulum dosyası bulunamadı: {localSetupPath}\nKurulum iptal edildi.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.1. WireSock kurulum dosyası bulundu: {localSetupPath}\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.2. Kurulum yolu belirleniyor...\n");
                // En uygun kurulum yolunu belirle
                var installPath = GetBestInstallPath();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.2. Kurulum yolu belirlendi: {installPath}\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3. WireSock kurulum işlemi başlatılıyor...\n");
                
                // Sadece /S parametresi ile kurulum yap
                try
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.1. Kurulum komutu: {localSetupPath} /S\n");
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = localSetupPath,
                            Arguments = "/S",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.2. Kurulum süreci başlatılıyor...\n");
                    process.Start();
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.3. Kurulum süreci başlatıldı, bekleniyor...\n");
                    await process.WaitForExitAsync();
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.4. Kurulum süreci tamamlandı. Exit Code: {process.ExitCode}\n");

                    if (process.ExitCode == 0)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.5. Kurulum başarılı (Exit Code: 0)\n");
                        System.Windows.MessageBox.Show($"WireSock Secure Connect sessiz kurulumu tamamlandı.\nKuruluma devam ediliyor...", 
                            "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.5. Kurulum Exit Code: {process.ExitCode}, kurulum doğrulanıyor...\n");
                        
                        // Check if WireSock is actually installed despite the error
                        if (_wireSockService.IsWireSockInstalled())
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.6. Kurulum doğrulandı (Exit Code: {process.ExitCode})\n");
                            System.Windows.MessageBox.Show($"WireSock Secure Connect kurulumu tamamlandı (Exit Code: {process.ExitCode}).\nKuruluma devam ediliyor...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.6. UYARI: Kurulum doğrulanamadı (Exit Code: {process.ExitCode})\n");
                            System.Windows.MessageBox.Show($"WireSock Secure Connect kurulumu tamamlandı ancak kurulum doğrulanamadı (Exit Code: {process.ExitCode}).\nKuruluma devam ediliyor...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3. HATA: Kurulum sırasında hata oluştu: {ex.Message}\n");
                    System.Windows.MessageBox.Show($"WireSock kurulumu sırasında hata oluştu: {ex.Message}\nKuruluma devam ediliyor...", 
                        "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                }



                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4. Kurulum sonrası temizlik işlemleri başlatılıyor...\n");
                
                // Kurulum tamamlandıktan sonra WiresockConnect.exe'yi kapat
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.1. WiresockConnect.exe süreçleri kapatılıyor...\n");
                var wiresockProcesses = Process.GetProcessesByName("WiresockConnect");
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.1. Bulunan WiresockConnect süreçleri: {wiresockProcesses.Length}\n");
                
                foreach (var proc in wiresockProcesses)
                {
                    try
                    {
                        proc.Kill();
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.1. WiresockConnect süreci kapatıldı: {proc.Id}\n");
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.1. WiresockConnect süreci kapatılırken hata: {ex.Message}\n");
                    }
                }
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.2. WireSock durumu güncelleniyor...\n");
                await UpdateWireSockStatusAsync();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4.2. WireSock durumu güncellendi.\n");
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.4. Kurulum sonrası temizlik işlemleri tamamlandı.\n");
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2. WireSock kurulum işlemi tamamlandı.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2. HATA: WireSock kurulumu sırasında hata oluştu: {ex.Message}\n");
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
                    File.AppendAllText(logPath, "DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }
                else
                {
                    File.AppendAllText(logPath, "DNS ayarları başarıyla yapıldı.\n");
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
                    File.AppendAllText(logPath, "DNS ayarları başarısız oldu. Kurulum devam ediyor...\n");
                }
                else
                {
                    File.AppendAllText(logPath, "DNS ayarları başarıyla yapıldı.\n");
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

        private string GetStandardSetupLogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "setup_standard.log");
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
            var standardLogPath = GetStandardSetupLogPath();
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. DNS ayarları timeout ile başlatılıyor (60 saniye)...\n");
                
                // 60 saniye timeout ile DNS ayarlarını yap
                var timeoutTask = Task.Run(() =>
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
# Fiziksel ağ adaptörlerini al
$adapters = Get-NetAdapter -Physical

# Her adaptör için DNS ayarlarını yap
foreach ($adapter in $adapters) {
    $adapterName = $adapter.Name
    $adapterGuid = $adapter.InterfaceGuid
    
    Write-Host ""Adaptör: $adapterName (GUID: $adapterGuid)""
    
    # IPv4 DNS ayarları
    try {
        Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -ServerAddresses '8.8.8.8', '9.9.9.9' -ErrorAction Stop
        Write-Host ""IPv4 DNS ayarları başarılı: $adapterName""
    }
    catch {
        Write-Host ""IPv4 DNS ayarları başarısız: $adapterName - $($_.Exception.Message)""
    }
    
    # IPv6 DNS ayarları
    try {
        Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv6 -ServerAddresses '2001:4860:4860::8888', '2620:fe::9' -ErrorAction Stop
        Write-Host ""IPv6 DNS ayarları başarılı: $adapterName""
    }
    catch {
        Write-Host ""IPv6 DNS ayarları başarısız: $adapterName - $($_.Exception.Message)""
    }
    
    # DoH ayarları için registry yolu
    $dohPath = 'HKLM:System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\' + $adapterGuid + '\DohInterfaceSettings'
    
    try {
        # Mevcut DoH ayarlarını temizle
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
        
        Write-Host ""DoH ayarları başarılı: $adapterName""
    }
    catch {
        Write-Host ""DoH ayarları başarısız: $adapterName - $($_.Exception.Message)""
    }
}

# DNS önbelleğini temizle
Clear-DnsClientCache
Write-Host ""DNS ayarları tamamlandı.""
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

                        File.AppendAllText(logPath, $"DNS Ayarı Bitiş: {DateTime.Now}\n");
                        return allCommandsSuccessful;
                    }
                    catch (Exception ex)
                    {
                        var logPath = GetDNSLogPath();
                        File.AppendAllText(logPath, $"DNS Ayarı Hatası: {ex.Message}\n");
                        Debug.WriteLine($"DNS ayar hatası: {ex.Message}");
                        return false;
                    }
                });

                // 60 saniye timeout ile bekle
                var timeout = TimeSpan.FromSeconds(60);
                var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(timeout));

                if (completedTask == timeoutTask)
                {
                    // DNS ayarları tamamlandı
                    var result = await timeoutTask;
                    if (result)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. DNS ayarları başarıyla tamamlandı.\n");
                        return true;
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. DNS ayarları başarısız oldu.\n");
                        return false;
                    }
                }
                else
                {
                    // Timeout oluştu - Yedek DNS ayarlarını dene
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. DNS ayarları timeout (60 saniye) - YEDEK DNS AYARLARI DENENİYOR.\n");
                    
                    var backupResult = await SetBackupDNSSettingsAsync();
                    if (backupResult)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. YEDEK DNS AYARLARI BAŞARILI - kuruluma devam ediliyor.\n");
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. YEDEK DNS AYARLARI DA BAŞARISIZ - kuruluma devam ediliyor.\n");
                    }
                    
                    return false; // False döndür ama kuruluma devam et
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.2.1. HATA: DNS ayarları sırasında hata oluştu: {ex.Message}\n");
                return false;
            }
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
            return await Task.Run(() =>
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

                    bool allFilesCleaned = true;
                    int cleanedFolders = 0;

                    // Discord base path'i bul
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var discordBasePath = Path.Combine(localAppData, "Discord");

                    if (Directory.Exists(discordBasePath))
                    {
                        // Tüm app-* klasörlerini bul
                        var appDirectories = Directory.GetDirectories(discordBasePath, "app-*");
                        File.AppendAllText(logPath, $"{appDirectories.Length} adet app-* klasörü bulundu.\n");

                        // Her app-* klasöründe Discord.exe var mı kontrol et ve temizle
                        foreach (var appDir in appDirectories)
                        {
                            var discordExePath = Path.Combine(appDir, "Discord.exe");
                            if (File.Exists(discordExePath))
                            {
                                File.AppendAllText(logPath, $"Discord.exe bulundu: {appDir}\n");
                                
                                var versionDllPath = Path.Combine(appDir, "version.dll");
                                var droverIniPath = Path.Combine(appDir, "drover.ini");

                                bool folderCleaned = true;

                                // version.dll sil
                                if (File.Exists(versionDllPath))
                                {
                                    try
                                    {
                                        File.Delete(versionDllPath);
                                        File.AppendAllText(logPath, $"version.dll silindi: {versionDllPath}\n");
                                    }
                                    catch (Exception ex)
                                    {
                                        File.AppendAllText(logPath, $"version.dll silinirken hata ({appDir}): {ex.Message}\n");
                                        folderCleaned = false;
                                    }
                                }

                                // drover.ini sil
                                if (File.Exists(droverIniPath))
                                {
                                    try
                                    {
                                        File.Delete(droverIniPath);
                                        File.AppendAllText(logPath, $"drover.ini silindi: {droverIniPath}\n");
                                    }
                                    catch (Exception ex)
                                    {
                                        File.AppendAllText(logPath, $"drover.ini silinirken hata ({appDir}): {ex.Message}\n");
                                        folderCleaned = false;
                                    }
                                }

                                if (folderCleaned)
                                {
                                    cleanedFolders++;
                                    File.AppendAllText(logPath, $"Klasör başarıyla temizlendi: {appDir}\n");
                                }
                                else
                                {
                                    allFilesCleaned = false;
                                    File.AppendAllText(logPath, $"Klasör temizlenemedi: {appDir}\n");
                                }
                            }
                        }

                        File.AppendAllText(logPath, $"Toplam {cleanedFolders} adet klasörden drover dosyaları temizlendi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"Discord base path bulunamadı: {discordBasePath}\n");
                        allFilesCleaned = false;
                    }

                    if (allFilesCleaned)
                    {
                        File.AppendAllText(logPath, "Tüm drover dosyaları başarıyla temizlendi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Bazı drover dosyaları temizlenemedi.\n");
                    }

                    return allFilesCleaned;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Drover dosyaları temizleme hatası: {ex.Message}\n");
                    return false;
                }
            });
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

                    bool allFilesCopied = true;
                    int copiedFolders = 0;

                    // Discord base path'i bul
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var discordBasePath = Path.Combine(localAppData, "Discord");

                    if (Directory.Exists(discordBasePath))
                    {
                        // Tüm app-* klasörlerini bul
                        var appDirectories = Directory.GetDirectories(discordBasePath, "app-*");
                        File.AppendAllText(logPath, $"{appDirectories.Length} adet app-* klasörü bulundu.\n");

                        // Her app-* klasöründe Discord.exe var mı kontrol et ve kopyala
                        foreach (var appDir in appDirectories)
                        {
                            var discordExePath = Path.Combine(appDir, "Discord.exe");
                            if (File.Exists(discordExePath))
                            {
                                File.AppendAllText(logPath, $"Discord.exe bulundu: {appDir}\n");
                                
                                // Hedef yolları
                                var versionDllTarget = Path.Combine(appDir, "version.dll");
                                var droverIniTarget = Path.Combine(appDir, "drover.ini");

                                bool folderCopied = true;

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
                                        File.AppendAllText(logPath, $"version.dll kopyalama hatası ({appDir}): {ex.Message}\n");
                                        folderCopied = false;
                                    }
                                }
                                else
                                {
                                    File.AppendAllText(logPath, $"version.dll kaynak dosyası bulunamadı: {versionDllSource}\n");
                                    folderCopied = false;
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
                                        File.AppendAllText(logPath, $"drover.ini kopyalama hatası ({appDir}): {ex.Message}\n");
                                        folderCopied = false;
                                    }
                                }
                                else
                                {
                                    File.AppendAllText(logPath, $"drover.ini kaynak dosyası bulunamadı: {droverIniSource}\n");
                                    folderCopied = false;
                                }

                                if (folderCopied)
                                {
                                    copiedFolders++;
                                    File.AppendAllText(logPath, $"Klasör başarıyla kopyalandı: {appDir}\n");
                                }
                                else
                                {
                                    allFilesCopied = false;
                                    File.AppendAllText(logPath, $"Klasör kopyalanamadı: {appDir}\n");
                                }
                            }
                        }

                        File.AppendAllText(logPath, $"Toplam {copiedFolders} adet klasöre drover dosyaları kopyalandı.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"Discord base path bulunamadı: {discordBasePath}\n");
                        allFilesCopied = false;
                    }

                    if (allFilesCopied)
                    {
                        File.AppendAllText(logPath, "Tüm drover dosyaları başarıyla kopyalandı.\n");
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

        /// <summary>
        /// Yedek DNS ayarlarını CMD komutları ile uygular (PowerShell ve Registry kullanmadan)
        /// </summary>
        private async Task<bool> SetBackupDNSSettingsAsync()
        {
            var standardLogPath = GetStandardSetupLogPath();
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] YEDEK DNS AYARLARI BAŞLATILIYOR (60 saniye timeout)...\n");
                
                // 60 saniye timeout ile yedek DNS ayarlarını yap
                var timeoutTask = Task.Run(() =>
                {
                    try
                    {
                        var logPath = GetLogPath();
                        File.AppendAllText(logPath, $"=== YEDEK DNS AYARLARI BAŞLATILIYOR: {DateTime.Now} ===\n");
                        
                        bool allCommandsSuccessful = true;

                    // 1. Mevcut ağ adaptörlerini listele
                    File.AppendAllText(logPath, "1. Mevcut ağ adaptörleri listeleniyor...\n");
                    var interfacesOutput = ExecuteCommandString("netsh", "interface show interface");
                    File.AppendAllText(logPath, $"Ağ adaptörleri:\n{interfacesOutput}\n");

                    // 2. Ethernet ve Wi-Fi adaptörlerini bul
                    var lines = interfacesOutput.Split('\n');
                    var targetInterfaces = new List<string>();

                    foreach (var line in lines)
                    {
                        if (line.Contains("Ethernet") || line.Contains("Wi-Fi"))
                        {
                            // Interface adını çıkar (genellikle 3. sütunda)
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                var interfaceName = parts[3].Trim(); // Satır sonu karakterlerini temizle
                                if (interfaceName.Contains("Ethernet") || interfaceName.Contains("Wi-Fi"))
                                {
                                    targetInterfaces.Add(interfaceName);
                                    File.AppendAllText(logPath, $"Hedef adaptör bulundu: {interfaceName}\n");
                                }
                            }
                        }
                    }

                    if (targetInterfaces.Count == 0)
                    {
                        File.AppendAllText(logPath, "HATA: Ethernet veya Wi-Fi adaptörü bulunamadı!\n");
                        return false;
                    }

                    // 3. Her adaptör için DNS ayarlarını yap
                    foreach (var interfaceName in targetInterfaces)
                    {
                        File.AppendAllText(logPath, $"3. {interfaceName} adaptörü için DNS ayarları yapılıyor...\n");

                        // IPv4 DNS ayarları
                        var ipv4Result = ExecuteCommand("netsh", $"interface ip set dns \"{interfaceName}\" static 8.8.8.8");
                        File.AppendAllText(logPath, $"IPv4 birincil DNS (8.8.8.8) ayarlandı: {ipv4Result}\n");

                        var ipv4SecondaryResult = ExecuteCommand("netsh", $"interface ip add dns \"{interfaceName}\" 9.9.9.9 index=2");
                        File.AppendAllText(logPath, $"IPv4 ikincil DNS (9.9.9.9) ayarlandı: {ipv4SecondaryResult}\n");

                        // IPv6 DNS ayarları
                        var ipv6Result = ExecuteCommand("netsh", $"interface ipv6 set dns \"{interfaceName}\" static 2001:4860:4860::8888");
                        File.AppendAllText(logPath, $"IPv6 birincil DNS (2001:4860:4860::8888) ayarlandı: {ipv6Result}\n");

                        var ipv6SecondaryResult = ExecuteCommand("netsh", $"interface ipv6 add dns \"{interfaceName}\" 2620:fe::9 index=2");
                        File.AppendAllText(logPath, $"IPv6 ikincil DNS (2620:fe::9) ayarlandı: {ipv6SecondaryResult}\n");

                        // DNS önbelleğini temizle
                        var flushResult = ExecuteCommand("ipconfig", "/flushdns");
                        File.AppendAllText(logPath, $"DNS önbelleği temizlendi: {flushResult}\n");

                        // DoH ayarlarını etkinleştir (Windows 11 için)
                        File.AppendAllText(logPath, $"4. {interfaceName} için DoH ayarları yapılıyor...\n");
                        
                        // DoH ayarlarını etkinleştir (sadece bir kez)
                        if (interfaceName == targetInterfaces[0]) // Sadece ilk adaptör için DoH ayarlarını yap
                        {
                            File.AppendAllText(logPath, "4.1. DoH otomatik şablon ayarları yapılıyor...\n");
                            
                            // DoH'u etkinleştir
                            var dohResult = ExecuteCommand("netsh", $"dns add global doh=yes");
                            File.AppendAllText(logPath, $"DoH global ayarı: {dohResult}\n");

                            var dotResult = ExecuteCommand("netsh", $"dns add global dot=yes");
                            File.AppendAllText(logPath, $"DoT global ayarı: {dotResult}\n");

                            // PowerShell ile DoH ayarları
                            File.AppendAllText(logPath, "4.2. PowerShell ile DoH ayarları yapılıyor...\n");
                            
                            // PowerShell script ile DoH ayarları
                            var psDohScript = @"
# DoH'u etkinleştir
Set-DnsClientDohServerAddress -ServerAddress '8.8.8.8' -DohTemplate 'https://dns.google/dns-query' -AllowFallbackToUdp $true
Set-DnsClientDohServerAddress -ServerAddress '9.9.9.9' -DohTemplate 'https://dns.quad9.net/dns-query' -AllowFallbackToUdp $true
Set-DnsClientDohServerAddress -ServerAddress '2001:4860:4860::8888' -DohTemplate 'https://dns.google/dns-query' -AllowFallbackToUdp $true
Set-DnsClientDohServerAddress -ServerAddress '2620:fe::9' -DohTemplate 'https://dns.quad9.net/dns-query' -AllowFallbackToUdp $true

# DoH'u global olarak etkinleştir
Set-DnsClientDohServerAddress -ServerAddress '8.8.8.8' -DohTemplate 'https://dns.google/dns-query' -AllowFallbackToUdp $true -AutoUpgrade $true
Set-DnsClientDohServerAddress -ServerAddress '9.9.9.9' -DohTemplate 'https://dns.quad9.net/dns-query' -AllowFallbackToUdp $true -AutoUpgrade $true
";

                            var psDohResult = ExecutePowerShellScript(psDohScript);
                            File.AppendAllText(logPath, $"PowerShell DoH ayarları: {psDohResult}\n");

                            // DNS client servisini yeniden başlat
                            File.AppendAllText(logPath, "4.3. DNS client servisi yeniden başlatılıyor...\n");
                            var restartDns = ExecuteCommand("net", "stop dnscache");
                            File.AppendAllText(logPath, $"DNS servisi durduruldu: {restartDns}\n");
                            
                            var startDns = ExecuteCommand("net", "start dnscache");
                            File.AppendAllText(logPath, $"DNS servisi başlatıldı: {startDns}\n");
                        }
                    }

                    // 5. PowerShell ile DoH şablonlarını ayarla
                    File.AppendAllText(logPath, "5. PowerShell ile DoH şablonları ayarlanıyor...\n");
                    
                    // PowerShell script ile DoH şablonları
                    var psDohTemplatesScript = @"
# Mevcut DoH ayarlarını temizle
Get-DnsClientDohServerAddress | Remove-DnsClientDohServerAddress -Force

# Google DNS için DoH şablonu
Set-DnsClientDohServerAddress -ServerAddress '8.8.8.8' -DohTemplate 'https://dns.google/dns-query' -AllowFallbackToUdp $true

# Quad9 DNS için DoH şablonu
Set-DnsClientDohServerAddress -ServerAddress '9.9.9.9' -DohTemplate 'https://dns.quad9.net/dns-query' -AllowFallbackToUdp $true

# Google DNS IPv6 için DoH şablonu
Set-DnsClientDohServerAddress -ServerAddress '2001:4860:4860::8888' -DohTemplate 'https://dns.google/dns-query' -AllowFallbackToUdp $true

# Quad9 DNS IPv6 için DoH şablonu
Set-DnsClientDohServerAddress -ServerAddress '2620:fe::9' -DohTemplate 'https://dns.quad9.net/dns-query' -AllowFallbackToUdp $true

# DoH ayarlarını doğrula
Get-DnsClientDohServerAddress
";

                    var psDohTemplatesResult = ExecutePowerShellScript(psDohTemplatesScript);
                    File.AppendAllText(logPath, $"PowerShell DoH şablonları: {psDohTemplatesResult}\n");

                    // 6. DNS ayarlarını doğrula
                    File.AppendAllText(logPath, "6. DNS ayarları doğrulanıyor...\n");
                    var verificationOutput = ExecuteCommandString("ipconfig", "/all");
                    File.AppendAllText(logPath, $"IP yapılandırması:\n{verificationOutput}\n");

                    // 7. DoH durumunu kontrol et
                    var dohStatusOutput = ExecuteCommandString("netsh", "dns show global");
                    File.AppendAllText(logPath, $"DoH durumu:\n{dohStatusOutput}\n");

                    // 8. DoH şablonlarını kontrol et
                    var dohTemplatesOutput = ExecuteCommandString("netsh", "dns show global doh");
                    File.AppendAllText(logPath, $"DoH şablonları:\n{dohTemplatesOutput}\n");

                        File.AppendAllText(logPath, $"=== YEDEK DNS AYARLARI TAMAMLANDI: {DateTime.Now} ===\n");
                        File.AppendAllText(logPath, $"Genel sonuç: {(allCommandsSuccessful ? "BAŞARILI" : "KISMEN BAŞARILI")}\n");

                        return allCommandsSuccessful;
                    }
                    catch (Exception ex)
                    {
                        var logPath = GetLogPath();
                        File.AppendAllText(logPath, $"Yedek DNS ayarları hatası: {ex.Message}\n");
                        File.AppendAllText(logPath, $"Stack Trace: {ex.StackTrace}\n");
                        return false;
                    }
                });

                // 60 saniye timeout ile bekle
                var timeout = TimeSpan.FromSeconds(60);
                var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(timeout));

                if (completedTask == timeoutTask)
                {
                    // Yedek DNS ayarları tamamlandı
                    var result = await timeoutTask;
                    if (result)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] YEDEK DNS AYARLARI BAŞARIYLA TAMAMLANDI.\n");
                        return true;
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] YEDEK DNS AYARLARI BAŞARISIZ OLDU.\n");
                        return false;
                    }
                }
                else
                {
                    // Timeout oluştu
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] YEDEK DNS AYARLARI TIMEOUT (60 saniye) - KURULUMA DEVAM EDİLİYOR.\n");
                    return false; // False döndür ama kuruluma devam et
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Yedek DNS ayarları sırasında hata oluştu: {ex.Message}\n");
                return false;
            }
        }
    }
} 