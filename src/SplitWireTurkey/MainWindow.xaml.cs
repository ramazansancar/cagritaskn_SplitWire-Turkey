using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using Microsoft.Win32;
using SplitWireTurkey.Services;
using System.Runtime.InteropServices;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

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

        // Görev çubuğu karanlık mod için P/Invoke tanımları
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

        // Görev çubuğu karanlık mod için sabitler
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;

        private readonly WireGuardService _wireGuardService;
        private readonly WireSockService _wireSockService;
        private readonly List<string> _folders;
        private readonly List<string> _zapretPresets;
        
        // Sekme boyut yönetimi için değişkenler
        private double _mainPageBaseHeight = 610;
        private double _mainPageAdvancedSettingsHeight = 870;
        private double _byeDPIHeight = 640;
        private double _zapretBaseHeight = 660;
        private double _zapretManualParamsHeight = 730;
        private double _goodbyeDPIBaseHeight = 550;
        private double _goodbyeDPIManualParamsHeight = 80;
        private double _goodbyeDPIUseBlacklistHeight = 55;
        private double _goodbyeDPIEditBlacklistHeight = 120;
        private double _advancedHeight = 780;
        
        // Switch durumları
        private bool _mainPageAdvancedSettingsActive = false;
        
        // Kaspersky overlay için klasör yolları
        public string CurrentProgramDirectory { get; private set; }
        public string LocalAppDataSplitWirePath { get; private set; }
        
        // WinDivert dosya kontrolü için kritik dosya yolları
        private readonly string[] _criticalWinDivertFiles;
        private bool _zapretManualParamsActive = false;
        private bool _goodbyeDPIManualParamsActive = false;
        private bool _goodbyeDPIUseBlacklistActive = false;
        private bool _goodbyeDPIEditBlacklistActive = false;

        // Görev çubuğu karanlık mod desteği
        private bool _isTaskbarDarkModeSupported = false;

        // Overlay kontrol değişkenleri
        private bool _isKasperskyDetected = false;
        private bool _isKasperskyVpnDetected = false;
        private bool _isCloudflareWarpDetected = false;

        public MainWindow()
        {
            InitializeComponent();
            
            _wireGuardService = new WireGuardService();
            _wireSockService = new WireSockService();
            _folders = new List<string>();
            _zapretPresets = new List<string>();
            
            // Görev çubuğu karanlık mod desteğini kontrol et
            CheckTaskbarDarkModeSupport();
            
            // Kaspersky overlay için klasör yollarını ayarla
            CurrentProgramDirectory = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
            LocalAppDataSplitWirePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitWire-Turkey");
            
            // Kritik WinDivert dosya yollarını tanımla
            _criticalWinDivertFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "zapret", "arm64", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86_64", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86", "WinDivert32.sys")
            };
            
            // Kaspersky ve WARP kontrollerini başlat
            _ = Task.Run(async () => await CheckCompatibilityAsync());
            
            UpdateWireSockStatus();
            CheckZapretFilesExist(); // Sadece kontrol yap, kopyalama yapma
            LoadZapretPresets();
            LoadGoodbyeDPIPresets();
            CheckAllServices(); // Yeni eklenen servis kontrolü
            
            // Varsayılan olarak aydınlık modda başlat
            btnThemeToggle.IsChecked = false;
            
            // Window yüklendikten sonra overlay metinlerini güncelle
            this.Loaded += MainWindow_Loaded;
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Kaspersky overlay metinlerini güncelle
            if (currentDirRun != null)
                currentDirRun.Text = $" {CurrentProgramDirectory} ";
            if (localAppDataRun != null)
                localAppDataRun.Text = $" {LocalAppDataSplitWirePath} ";
        }
        
        private void UpdateKasperskyOverlayColors(bool isDarkMode)
        {
            try
            {
                if (currentDirRun != null)
                {
                    var color = isDarkMode ? 
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#24bdff") :
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#003dc2");
                    currentDirRun.Foreground = new System.Windows.Media.SolidColorBrush(color);
                }
                
                if (localAppDataRun != null)
                {
                    var color = isDarkMode ? 
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#24bdff") :
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#003dc2");
                    localAppDataRun.Foreground = new System.Windows.Media.SolidColorBrush(color);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Kaspersky overlay renk güncelleme hatası: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Kritik WinDivert dosyalarının varlığını kontrol eder
        /// </summary>
        /// <returns>Eğer kritik dosyalardan herhangi biri eksikse true, hepsi mevcutsa false</returns>
        private bool AreCriticalWinDivertFilesMissing()
        {
            try
            {
                // Her dosya için detaylı kontrol yap
                var missingFiles = new List<string>();
                foreach (var path in _criticalWinDivertFiles)
                {
                    var exists = File.Exists(path);
                    Debug.WriteLine($"Kritik dosya kontrolü: {path} - Mevcut: {exists}");
                    if (!exists)
                    {
                        missingFiles.Add(path);
                    }
                }
                
                var result = missingFiles.Any();
                Debug.WriteLine($"Kritik WinDivert dosya kontrolü sonucu: {result} (Eksik dosyalar: {string.Join(", ", missingFiles)})");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Kritik WinDivert dosya kontrolü hatası: {ex.Message}");
                return true; // Hata durumunda güvenli tarafta kal
            }
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
                txtWiresockStatus.Text = "";
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

        private Dictionary<string, bool> _serviceStatusCache = new Dictionary<string, bool>();
        private DateTime _lastServiceCheck = DateTime.MinValue;
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromSeconds(2); // 2 saniye cache

        private async void CheckAllServices()
        {
            // Cache süresi dolmamışsa cache'den oku
            if (DateTime.Now - _lastServiceCheck < _cacheTimeout && _serviceStatusCache.Count > 0)
            {
                UpdateAllServiceUIFromCache();
                return;
            }

            // Asenkron olarak servis durumlarını kontrol et
            await Task.Run(() => CheckAllServicesAsync());
        }

        private async Task CheckAllServicesAsync()
        {
            try
            {
                var serviceChecks = new List<Task<(string serviceName, bool isInstalled)>>();
                
                // Tüm servisleri paralel olarak kontrol et
                serviceChecks.Add(CheckServiceAsync("wiresock-client-service"));
                serviceChecks.Add(CheckServiceAsync("ByeDPI"));
                serviceChecks.Add(CheckServiceAsync("ProxiFyreService"));
                serviceChecks.Add(CheckServiceAsync("winws1"));
                serviceChecks.Add(CheckServiceAsync("winws2"));
                serviceChecks.Add(CheckServiceAsync("zapret"));
                serviceChecks.Add(CheckServiceAsync("GoodbyeDPI"));
                serviceChecks.Add(CheckServiceAsync("WinDivert"));

                // Tüm sonuçları bekle
                var results = await Task.WhenAll(serviceChecks);
                
                // Cache'i güncelle
                lock (_serviceStatusCache)
                {
                    _serviceStatusCache.Clear();
                    foreach (var result in results)
                    {
                        _serviceStatusCache[result.serviceName] = result.isInstalled;
                    }
                    _lastServiceCheck = DateTime.Now;
                }

                // UI'ı güncelle
                await Dispatcher.InvokeAsync(() => UpdateAllServiceUIFromCache());
                
                // Drover dosyalarını kontrol et
                await CheckDroverFilesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Servis kontrol hatası: {ex.Message}");
            }
        }

        private async Task<(string serviceName, bool isInstalled)> CheckServiceAsync(string serviceName)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"query {serviceName}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                bool isInstalled = output.Contains("SERVICE_NAME:") && !output.Contains("1060");
                return (serviceName, isInstalled);
            }
            catch
            {
                return (serviceName, false);
            }
        }

        private void UpdateAllServiceUIFromCache()
        {
            if (_serviceStatusCache.TryGetValue("wiresock-client-service", out bool wireSockInstalled))
                UpdateServiceUI(wireSockInstalled, wireSockStatus, wireSockStatusText, btnWireSockRemove);
            
            if (_serviceStatusCache.TryGetValue("ByeDPI", out bool byeDPIInstalled))
                UpdateServiceUI(byeDPIInstalled, byeDPIStatus, byeDPIStatusText, btnByeDPIRemove);
            
            if (_serviceStatusCache.TryGetValue("ProxiFyreService", out bool proxiFyreInstalled))
                UpdateServiceUI(proxiFyreInstalled, proxiFyreStatus, proxiFyreStatusText, btnProxiFyreRemove);
            
            if (_serviceStatusCache.TryGetValue("winws1", out bool winWS1Installed))
                UpdateServiceUI(winWS1Installed, winWS1Status, winWS1StatusText, btnWinWS1Remove);
            
            if (_serviceStatusCache.TryGetValue("winws2", out bool winWS2Installed))
                UpdateServiceUI(winWS2Installed, winWS2Status, winWS2StatusText, btnWinWS2Remove);
            
            if (_serviceStatusCache.TryGetValue("zapret", out bool zapretInstalled))
                UpdateServiceUI(zapretInstalled, zapretStatus, zapretStatusText, btnZapretServiceRemove);
            
            if (_serviceStatusCache.TryGetValue("GoodbyeDPI", out bool goodbyeDPIInstalled))
                UpdateServiceUI(goodbyeDPIInstalled, goodbyeDPIStatus, goodbyeDPIStatusText, btnGoodbyeDPIAdvancedRemove);
            
            if (_serviceStatusCache.TryGetValue("WinDivert", out bool winDivertInstalled))
                UpdateServiceUI(winDivertInstalled, winDivertStatus, winDivertStatusText, btnWinDivertRemove);
        }

        private async Task RefreshServiceStatusesAsync()
        {
            // Cache'i temizle ve yeniden kontrol et
            lock (_serviceStatusCache)
            {
                _serviceStatusCache.Clear();
                _lastServiceCheck = DateTime.MinValue;
            }
            
            // Asenkron olarak servis durumlarını kontrol et
            await CheckAllServicesAsync();
        }

        private void UpdateRemovedServiceStatus(string serviceName)
        {
            // Kaldırılan hizmetin durumunu hemen güncelle
            var statusEllipse = GetStatusEllipse(serviceName);
            var statusText = GetStatusTextBlock(serviceName);
            var removeButton = GetRemoveButton(serviceName);

            if (removeButton != null)
            {
                removeButton.Visibility = Visibility.Collapsed;
            }
            
            // ByeDPI için özel işlem - hem Gelişmiş sayfa hem de ByeDPI sayfası butonlarını gizle
            if (serviceName == "ByeDPI")
            {
                if (btnRemoveByeDPI != null)
                {
                    btnRemoveByeDPI.Visibility = Visibility.Collapsed;
                }
            }
            
            if (statusEllipse != null && statusText != null)
            {
                statusEllipse.Fill = System.Windows.Media.Brushes.Red;
                statusText.Text = "Yüklü değil";
            }
        }

        private async Task ForceRefreshAllServicesAsync()
        {
            // Cache'i temizle ve yeniden kontrol et
            lock (_serviceStatusCache)
            {
                _serviceStatusCache.Clear();
                _lastServiceCheck = DateTime.MinValue;
            }
            
            // 1 saniye bekle ve yeniden kontrol et
            await Task.Delay(1000);
            await CheckAllServicesAsync();
        }

                // Eski CheckService metodu kaldırıldı - artık CheckServiceAsync kullanılıyor

        private void UpdateServiceUI(bool isInstalled, System.Windows.Shapes.Ellipse statusEllipse, TextBlock statusText, System.Windows.Controls.Button removeButton)
        {
            if (isInstalled)
            {
                if (statusEllipse != null) statusEllipse.Fill = System.Windows.Media.Brushes.Green;
                if (statusText != null) statusText.Text = "Yüklü";
                if (removeButton != null) removeButton.Visibility = Visibility.Visible;
            }
            else
            {
                if (statusEllipse != null) statusEllipse.Fill = System.Windows.Media.Brushes.Red;
                if (statusText != null) statusText.Text = "Yüklü değil";
                if (removeButton != null) removeButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateServiceUIError(System.Windows.Shapes.Ellipse statusEllipse, TextBlock statusText, System.Windows.Controls.Button removeButton)
        {
            if (statusEllipse != null) statusEllipse.Fill = System.Windows.Media.Brushes.Gray;
            if (statusText != null) statusText.Text = "Hata";
            if (removeButton != null) removeButton.Visibility = Visibility.Collapsed;
        }

        private async Task CheckDroverFilesAsync()
        {
            try
            {
                var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                bool droverExists = false;

                // Discord app-* klasörlerinde version.dll dosyasını ara
                if (Directory.Exists(discordPath))
                {
                    var appFolders = Directory.GetDirectories(discordPath, "app-*");
                    foreach (var appFolder in appFolders)
                    {
                        var versionDllPath = Path.Combine(appFolder, "version.dll");
                        if (File.Exists(versionDllPath))
                        {
                            droverExists = true;
                            break;
                        }
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (droverExists)
                    {
                        droverStatus.Fill = System.Windows.Media.Brushes.Green;
                        droverStatusText.Text = "Yüklü";
                        btnDroverRemove.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        droverStatus.Fill = System.Windows.Media.Brushes.Red;
                        droverStatusText.Text = "Yüklü değil";
                        btnDroverRemove.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Drover check error: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    droverStatus.Fill = System.Windows.Media.Brushes.Gray;
                    droverStatusText.Text = "Hata";
                    btnDroverRemove.Visibility = Visibility.Collapsed;
                });
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Standart kurulum başlatmak istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            // Standart kurulum log dosyasını başlat
            var standardLogPath = GetStandardSetupLogPath();
            File.WriteAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM BAŞLATILIYOR ===\n");
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1. AŞAMA: Kurulum öncesi temizlik başlatılıyor...\n");
                
                // Kurulum öncesi temizlik (Discord + tüm hizmetler)
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.1. Kurulum öncesi temizlik yapılıyor...\n");
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (cleanupSuccess)
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.1. Kurulum öncesi temizlik başarıyla tamamlandı.\n");
                }
                else
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1.1. UYARI: Kurulum öncesi temizlik sırasında hata oluştu.\n");
                }
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1. AŞAMA: Kurulum öncesi temizlik tamamlandı.\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2. AŞAMA: WireSock kurulum kontrolü başlatılıyor...\n");
                
                if (!_wireSockService.IsLatestWireSockInstalled())
                {
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.1. WireSock son sürümü yüklü değil. Kurulum gerekli.\n");
                    
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
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.1. WireSock son sürümü zaten yüklü.\n");
                }
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2. AŞAMA: WireSock kurulum kontrolü tamamlandı.\n");

                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. AŞAMA: Hızlı kurulum başlatılıyor...\n");
                await PerformFastSetup();
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. AŞAMA: Hızlı kurulum tamamlandı.\n");
                
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM BAŞARIYLA TAMAMLANDI ===\n");
                
                // WireSock kısayolunu sil
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock kısayolu temizleniyor...\n");
                RemoveWireSockShortcut(standardLogPath);
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock kısayolu temizleme tamamlandı.\n");
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Standart kurulum sırasında hata oluştu: {ex.Message}\n");
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === STANDART KURULUM HATA İLE SONLANDI ===\n");
                ShowLoading(false);
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
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
                // Kurulum öncesi temizlik (Discord + tüm hizmetler)
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (!cleanupSuccess)
                {
                    System.Windows.MessageBox.Show("Kurulum öncesi temizlik sırasında hata oluştu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await PerformAlternativeSetup();
            }
            finally
            {
                ShowLoading(false);
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
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



        private async void BtnRemoveAllServices_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "zapret, winws1, winws2, wiresock-client-service, GoodbyeDPI, WinDivert, ByeDPI, ProxiFyreService hizmetlerini durdurup kaldırmak, Discord klasöründeki drover dosyalarını silmek ve WireSockRefresh Task Scheduler görevini kaldırmak istediğinizden emin misiniz?",
                "Hizmetleri Kaldır", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                // Doğru sıralamayla hizmetleri kaldır (zapret, GoodbyeDPI, WinDivert sıralaması önemli)
                var services = new[] { "zapret", "GoodbyeDPI", "WinDivert", "winws1", "winws2", "wiresock-client-service", "ByeDPI", "ProxiFyreService" };
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
                
                // WireSockRefresh görevini kaldır
                File.AppendAllText(logPath, "WireSockRefresh Task Scheduler görevi kaldırılıyor...\n");
                var taskRemovalSuccess = await RemoveWireSockRefreshTaskAsync();
                if (taskRemovalSuccess)
                {
                    File.AppendAllText(logPath, "WireSockRefresh görevi başarıyla kaldırıldı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, "UYARI: WireSockRefresh görevi kaldırılamadı veya zaten mevcut değildi.\n");
                }
                
                System.Windows.MessageBox.Show("Tüm hizmetler, firewall kuralları, drover dosyaları ve WireSockRefresh görevi başarıyla kaldırıldı.", 
                    "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Hizmet listesini güncelle
                await RefreshServiceStatusesAsync();
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "Hakkında - SplitWire-Turkey",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Ana içerik
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            // Başlık
            var titleText = new TextBlock
            {
                Text = "SplitWire-Turkey v1.5 © 2025 Çağrı Taşkın",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // GitHub linki
            var githubText = new TextBlock
            {
                Text = "Daha fazla bilgi ve kullanım detayları için:",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(githubText);

            var githubLink = new TextBlock
            {
                Text = "SplitWire-Turkey Github",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.Blue,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 20)
            };
            githubLink.MouseLeftButtonDown += (s, args) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/cagritaskn/SplitWire-Turkey",
                    UseShellExecute = true
                });
            };
            contentStack.Children.Add(githubLink);

            // Recep Baltaş teşekkürü
            var recepText = new TextBlock
            {
                Text = "Yazılımın geliştirilmesine katkıda bulunan Techolay.net kurucusu Recep Baltaş'a çok teşekkür ederim. Techolay.net Sosyal'i ziyaret etmek için:",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(recepText);

            var techolayLink = new TextBlock
            {
                Text = "Techolay Sosyal",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.Blue,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 20)
            };
            techolayLink.MouseLeftButtonDown += (s, args) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://techolay.net/sosyal/",
                    UseShellExecute = true
                });
            };
            contentStack.Children.Add(techolayLink);

            // Bal Porsuğu teşekkürü
            var balText = new TextBlock
            {
                Text = "ByeDPI metodu ve Zapret presetleri için Bal Porsuğu'na teşekkürler. YouTube kanalını ziyaret etmek için:",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(balText);

            var youtubeLink = new TextBlock
            {
                Text = "Bal Porsuğu Youtube Kanalı",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.Blue,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 20)
            };
            youtubeLink.MouseLeftButtonDown += (s, args) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.youtube.com/@sauali",
                    UseShellExecute = true
                });
            };
            contentStack.Children.Add(youtubeLink);

            // Hata Raporları ve Tavsiyeler başlığı
            var errorReportsTitle = new TextBlock
            {
                Text = "Hata Raporları ve Tavsiyeler",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                Margin = new Thickness(0, 20, 0, 15),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(errorReportsTitle);

            // Hata raporları açıklaması
            var errorReportsText = new TextBlock
            {
                Text = "SplitWire-Turkey kullanımı sırasında herhangi bir hata, sorun veya öneriniz varsa, GitHub sayfasının Issues bölümünden rapor oluşturabilirsiniz. Rapor oluştururken mümkünse programın kurulu olduğu konumdaki logs klasöründe bulunan .log dosyalarını da ekleyerek daha detaylı bilgi sağlayabilirsiniz.",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(errorReportsText);

            // GitHub Issues linki
            var issuesText = new TextBlock
            {
                Text = "GitHub Issues sayfasına gitmek için:",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(issuesText);

            var issuesLink = new TextBlock
            {
                Text = "SplitWire-Turkey Issues",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.Blue,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 20)
            };
            issuesLink.MouseLeftButtonDown += (s, args) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/cagritaskn/SplitWire-Turkey/issues",
                    UseShellExecute = true
                });
            };
            contentStack.Children.Add(issuesLink);

            // New Issue açıklaması
            var newIssueText = new TextBlock
            {
                Text = "Issues sayfasında sağ üst kısımda bulunan 'New Issue' butonuna tıklayarak yeni bir hata raporu veya öneri oluşturabilirsiniz.",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(newIssueText);

            // Log dosyaları açıklaması
            var logFilesText = new TextBlock
            {
                Text = "Log dosyaları genellikle şu konumda bulunur: Program Files/SplitWire-Turkey/logs",
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(logFilesText);

            scrollViewer.Content = contentStack;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
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
                // Kurulum öncesi temizlik (Discord + tüm hizmetler)
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (!cleanupSuccess)
                {
                    System.Windows.MessageBox.Show("Kurulum öncesi temizlik sırasında hata oluştu. Kurulum devam ediyor...", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

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
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmetleri kaldırılıyor...\n");
                
                // Hizmetleri durdur
                File.AppendAllText(logPath, "GoodbyeDPI hizmeti durduruluyor...\n");
                var stopGoodbyeDPI = await ExecuteCommandAsync("net", "stop GoodbyeDPI");
                if (stopGoodbyeDPI == 0)
                {
                    File.AppendAllText(logPath, "GoodbyeDPI hizmeti başarıyla durduruldu.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"GoodbyeDPI hizmeti durdurulamadı (Exit Code: {stopGoodbyeDPI}).\n");
                }
                
                File.AppendAllText(logPath, "WinDivert hizmeti durduruluyor...\n");
                var stopWinDivert = await ExecuteCommandAsync("net", "stop WinDivert");
                if (stopWinDivert == 0)
                {
                    File.AppendAllText(logPath, "WinDivert hizmeti başarıyla durduruldu.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"WinDivert hizmeti durdurulamadı (Exit Code: {stopWinDivert}).\n");
                }
                
                // Hizmetleri kaldır
                File.AppendAllText(logPath, "GoodbyeDPI hizmeti siliniyor...\n");
                var removeGoodbyeDPI = await ExecuteCommandAsync("sc", "delete GoodbyeDPI");
                if (removeGoodbyeDPI == 0)
                {
                    File.AppendAllText(logPath, "GoodbyeDPI hizmeti başarıyla silindi.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"GoodbyeDPI hizmeti silinemedi (Exit Code: {removeGoodbyeDPI}).\n");
                }
                
                File.AppendAllText(logPath, "WinDivert hizmeti siliniyor...\n");
                var removeWinDivert = await ExecuteCommandAsync("sc", "delete WinDivert");
                if (removeWinDivert == 0)
                {
                    File.AppendAllText(logPath, "WinDivert hizmeti başarıyla silindi.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"WinDivert hizmeti silinemedi (Exit Code: {removeWinDivert}).\n");
                }
                
                // Hizmet kaldırma işlemlerinin tamamlanmasını bekle
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmet kaldırma işlemlerinin tamamlanması bekleniyor...\n");
                await Task.Delay(1000); // 1 saniye bekle
                
                // Hizmetlerin gerçekten kaldırıldığını doğrula
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmet kaldırma işlemleri doğrulanıyor...\n");
                
                var checkGoodbyeDPI = await ExecuteCommandAsync("sc", "query GoodbyeDPI");
                if (checkGoodbyeDPI == 0)
                {
                    File.AppendAllText(logPath, "UYARI: GoodbyeDPI hizmeti hala mevcut!\n");
                }
                else
                {
                    File.AppendAllText(logPath, "GoodbyeDPI hizmeti başarıyla kaldırıldı.\n");
                }
                
                var checkWinDivert = await ExecuteCommandAsync("sc", "query WinDivert");
                if (checkWinDivert == 0)
                {
                    File.AppendAllText(logPath, "UYARI: WinDivert hizmeti hala mevcut!\n");
                }
                else
                {
                    File.AppendAllText(logPath, "WinDivert hizmeti başarıyla kaldırıldı.\n");
                }
                
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmet kaldırma işlemleri tamamlandı.\n");
                return true; // Hata olsa bile true döndür çünkü hizmet zaten kaldırılmış olabilir
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"GoodbyeDPI hizmet kaldırma hatası: {ex.Message}\n");
                Debug.WriteLine($"GoodbyeDPI hizmet kaldırma hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RemoveAllServicesAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, "Tüm hizmetler kaldırılıyor...\n");
                
                // WireSock hizmetini kaldır
                File.AppendAllText(logPath, "WireSock hizmeti kaldırılıyor...\n");
                var wiresockRemoved = await _wireSockService.RemoveServiceAsync();
                File.AppendAllText(logPath, "WireSock hizmeti kaldırma tamamlandı.\n");
                
                // GoodbyeDPI ve WinDivert hizmetlerini kaldır
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmetleri kaldırılıyor...\n");
                var goodbyeDPIRemoved = await RemoveGoodbyeDPIServicesAsync();
                File.AppendAllText(logPath, "GoodbyeDPI ve WinDivert hizmetleri kaldırma tamamlandı.\n");
                
                // Hizmetleri doğru sırayla durdur (zapret, GoodbyeDPI, WinDivert sıralaması önemli)
                var servicesToStop = new[] { "zapret", "GoodbyeDPI", "WinDivert", "winws1", "winws2", "wiresock-client-service", "ByeDPI", "ProxiFyreService" };
                
                File.AppendAllText(logPath, "Hizmetler durduruluyor...\n");
                foreach (var service in servicesToStop)
                {
                    try
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti durduruluyor...\n");
                        var result = await ExecuteCommandAsync("net", $"stop {service}");
                        if (result == 0)
                        {
                            File.AppendAllText(logPath, $"{service} hizmeti başarıyla durduruldu.\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, $"{service} hizmeti durdurulamadı (Exit Code: {result}).\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti durdurma hatası: {ex.Message}\n");
                    }
                }
                
                // Tüm hizmetleri sil (aynı sıralama)
                var servicesToDelete = new[] { "zapret", "GoodbyeDPI", "WinDivert", "winws1", "winws2", "wiresock-client-service", "ByeDPI", "ProxiFyreService" };
                
                File.AppendAllText(logPath, "Hizmetler siliniyor...\n");
                foreach (var service in servicesToDelete)
                {
                    try
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti siliniyor...\n");
                        var result = await ExecuteCommandAsync("sc", $"delete {service}");
                        if (result == 0)
                        {
                            File.AppendAllText(logPath, $"{service} hizmeti başarıyla silindi.\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, $"{service} hizmeti silinemedi (Exit Code: {result}).\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti silme hatası: {ex.Message}\n");
                    }
                }
                
                // Windows Firewall kurallarını da temizle
                File.AppendAllText(logPath, "Windows Firewall kuralları temizleniyor...\n");
                await RemoveFirewallRulesAsync();
                File.AppendAllText(logPath, "Windows Firewall kuralları temizleme tamamlandı.\n");
                
                // Hizmet kaldırma işlemlerinin tamamlanmasını bekle
                File.AppendAllText(logPath, "Hizmet kaldırma işlemlerinin tamamlanması bekleniyor...\n");
                await Task.Delay(2000); // 2 saniye bekle
                
                // Hizmetlerin gerçekten kaldırıldığını doğrula
                File.AppendAllText(logPath, "Hizmet kaldırma işlemleri doğrulanıyor...\n");
                foreach (var service in servicesToDelete)
                {
                    try
                    {
                        var checkResult = await ExecuteCommandAsync("sc", $"query {service}");
                        if (checkResult == 0)
                        {
                            File.AppendAllText(logPath, $"UYARI: {service} hizmeti hala mevcut!\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, $"{service} hizmeti başarıyla kaldırıldı.\n");
                        }
                    }
                    catch
                    {
                        File.AppendAllText(logPath, $"{service} hizmeti kontrol edilemedi.\n");
                    }
                }
                
                File.AppendAllText(logPath, "Tüm hizmet kaldırma işlemleri tamamlandı.\n");
                return true;
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"Hizmet kaldırma hatası: {ex.Message}\n");
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
                
                // Tarayıcı tünelleme ayarını kontrol et
                var includeBrowsers = chkBrowserTunneling.IsChecked == true;
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.3.1. Tarayıcı tünelleme: {(includeBrowsers ? "Açık" : "Kapalı")}\n");
                
                var success = await _wireGuardService.CreateProfileAsync(null, includeBrowsers);
                
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
                        
                        // WireSock Refresh görevini oluştur
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.5. WireSock Refresh görevi oluşturuluyor...\n");
                        var refreshTaskSuccess = await CreateWireSockRefreshTaskAsync();
                        if (refreshTaskSuccess)
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.5. WireSock Refresh görevi başarıyla oluşturuldu.\n");
                        }
                        else
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.5. UYARI: WireSock Refresh görevi oluşturulamadı.\n");
                        }
                        
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.6. Sistem yeniden başlatma mesajı gösteriliyor.\n");
                        ShowRestartMessage();
                        
                        // Ana kurulum başarılı sonrası tüm hizmet durumlarını güncelle
                        CheckAllServices();
                        
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3.6. Sistem yeniden başlatma mesajı gösterildi.\n");
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
                
                // Tarayıcı tünelleme ayarını kontrol et
                var includeBrowsers = chkBrowserTunneling.IsChecked == true;
                File.AppendAllText(logPath, $"Tarayıcı tünelleme: {(includeBrowsers ? "Açık" : "Kapalı")}\n");
                
                var success = await _wireGuardService.CreateProfileAsync(extraFolders, includeBrowsers);
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
                        
                        // WireSock Refresh görevini oluştur
                        File.AppendAllText(logPath, "WireSock Refresh görevi oluşturuluyor...\n");
                        var refreshTaskSuccess = await CreateWireSockRefreshTaskAsync();
                        if (refreshTaskSuccess)
                        {
                            File.AppendAllText(logPath, "WireSock Refresh görevi başarıyla oluşturuldu.\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, "UYARI: WireSock Refresh görevi oluşturulamadı.\n");
                        }
                        
                        File.AppendAllText(logPath, "Sistem yeniden başlatma mesajı gösteriliyor.\n");
                        ShowRestartMessage();
                        
                        // Ana kurulum başarılı sonrası tüm hizmet durumlarını güncelle
                        CheckAllServices();
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
                
                // Tarayıcı tünelleme ayarını kontrol et
                var includeBrowsers = chkBrowserTunneling.IsChecked == true;
                File.AppendAllText(logPath, $"Tarayıcı tünelleme: {(includeBrowsers ? "Açık" : "Kapalı")}\n");
                
                var success = await _wireGuardService.CreateProfileAsync(extraFolders, includeBrowsers);
                
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
                    // Hizmet zaten yüklü değilse işlemlere devam et
                    if (!_wireSockService.IsWireSockInstalled())
                    {
                        File.AppendAllText(logPath, "WireSock hizmeti zaten yüklü değil. İşlemlere devam ediliyor...\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Mevcut WireSock hizmeti kaldırılamadı.\n");
                        System.Windows.MessageBox.Show("Mevcut WireSock hizmeti kaldırılamadı. Alternatif kurulum başlatılamadı.", 
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "Mevcut WireSock hizmeti başarıyla kaldırıldı.\n");
                }

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
                
                // Tarayıcı tünelleme ayarını kontrol et
                var includeBrowsers = chkBrowserTunneling.IsChecked == true;
                File.AppendAllText(logPath, $"Tarayıcı tünelleme: {(includeBrowsers ? "Açık" : "Kapalı")}\n");
                
                var success = await _wireGuardService.CreateProfileAsync(null, includeBrowsers);
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
                        
                        // WireSock Refresh görevini oluştur
                        File.AppendAllText(logPath, "WireSock Refresh görevi oluşturuluyor...\n");
                        var refreshTaskSuccess = await CreateWireSockRefreshTaskAsync();
                        if (refreshTaskSuccess)
                        {
                            File.AppendAllText(logPath, "WireSock Refresh görevi başarıyla oluşturuldu.\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, "UYARI: WireSock Refresh görevi oluşturulamadı.\n");
                        }
                        
                        // WireSock kısayolunu sil
                        File.AppendAllText(logPath, "WireSock kısayolu temizleniyor...\n");
                        RemoveWireSockShortcut(logPath);
                        File.AppendAllText(logPath, "WireSock kısayolu temizleme tamamlandı.\n");
                        
                        ShowRestartMessage();
                        
                        // Ana kurulum başarılı sonrası tüm hizmet durumlarını güncelle
                        CheckAllServices();
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
            // Loading ekranını kapat
            
            
            var result = System.Windows.MessageBox.Show(
                "Kurulum başarıyla tamamlandı. Değişikliklerin uygulanabilmesi için sisteminizi yeniden başlatın. Şimdi yeniden başlatmak için Evet'e tıklayın. Daha sonra yeniden başlatmak için Hayır'a tıklayın.",
                "Kurulum Tamamlandı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                // Şimdi yeniden başlat
                RestartSystem();
                ShowLoading(false);
            }
            // No seçilirse sadece mesaj kutusu kapanır
            ShowLoading(false);
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


        private async Task<bool> RemoveWireSockRefreshTaskAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi kaldırılıyor...\n");
                
                // PowerShell komutunu hazırla
                var psCommand = @"
try {
    $existingTask = Get-ScheduledTask -TaskName ""WireSockRefresh"" -ErrorAction SilentlyContinue
    if ($existingTask) {
        Write-Host ""WireSockRefresh görevi bulundu, kaldırılıyor...""
        Unregister-ScheduledTask -TaskName ""WireSockRefresh"" -Confirm:$false
        Write-Host ""WireSockRefresh görevi başarıyla kaldırıldı""
        exit 0
    } else {
        Write-Host ""WireSockRefresh görevi bulunamadı""
        exit 0
    }
} catch {
    Write-Host ""Hata: $($_.Exception.Message)""
    exit 1
}";
                
                // PowerShell'i çalıştır
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas" // Yönetici izni ile çalıştır
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                File.AppendAllText(logPath, $"PowerShell çıktısı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"PowerShell hatası: {error}\n");
                }
                File.AppendAllText(logPath, $"PowerShell Exit Code: {process.ExitCode}\n");
                
                if (process.ExitCode == 0)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi kaldırma işlemi tamamlandı.\n");
                    return true;
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: WireSock Refresh görevi kaldırılamadı.\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi kaldırma hatası: {ex.Message}\n");
                return false;
            }
        }

        private async Task<bool> TestWireSockRefreshTaskAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh task testing...\n");
                
                // PowerShell komutunu hazırla
                var psCommand = @"
try {
    $task = Get-ScheduledTask -TaskName ""WireSockRefresh"" -ErrorAction SilentlyContinue
    if ($task) {
        $info = Get-ScheduledTaskInfo -TaskName ""WireSockRefresh""
        $result = @{
            TaskName = $task.TaskName
            State = $task.State
            LastRunTime = $info.LastRunTime
            NextRunTime = $info.NextRunTime
            LastTaskResult = $info.LastTaskResult
            NumberOfMissedRuns = $info.NumberOfMissedRuns
        }
        $result | ConvertTo-Json
        exit 0
    } else {
        Write-Host ""Görev bulunamadı""
        exit 1
    }
} catch {
    Write-Host ""Hata: $($_.Exception.Message)""
    exit 1
}";
                
                // PowerShell'i çalıştır
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas"
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                File.AppendAllText(logPath, $"Test PowerShell çıktısı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"Test PowerShell hatası: {error}\n");
                }
                
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi test hatası: {ex.Message}\n");
                return false;
            }
        }

        private async Task<bool> CreateWireSockRefreshTaskAsync()
        {
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi oluşturuluyor...\n");
                
                // wiresock_refresh.bat dosyasının yolunu al
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var exeDirectory = Path.GetDirectoryName(exePath);
                var batchPath = Path.Combine(exeDirectory, "res", "wiresock_refresh.bat");
                
                if (!File.Exists(batchPath))
                {
                    File.AppendAllText(logPath, $"HATA: wiresock_refresh.bat dosyası bulunamadı: {batchPath}\n");
                    return false;
                }
                
                File.AppendAllText(logPath, $"wiresock_refresh.bat dosyası bulundu: {batchPath}\n");
                
                // PowerShell komutunu hazırla - Farklı yaklaşım
                var escapedBatchPath = batchPath.Replace("\"", "`\"");
                var psCommand = $@"
try {{
    # Önce mevcut görevi kontrol et ve varsa kaldır
    $existingTask = Get-ScheduledTask -TaskName 'WireSockRefresh' -ErrorAction SilentlyContinue
    if ($existingTask) {{
        Write-Host 'Existing WireSockRefresh task found, removing...'
        Unregister-ScheduledTask -TaskName 'WireSockRefresh' -Confirm:$false
        Write-Host 'Existing task removed'
        Start-Sleep -Seconds 2
    }}
    
    # Batch dosya yolunu değişkene ata
    $BatchPath = '{escapedBatchPath}'
    
    # Yeni görevi oluştur - Direkt batch dosyası çalıştır
    $Action = New-ScheduledTaskAction -Execute $BatchPath
    $Trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 3) -RepetitionDuration (New-TimeSpan -Days 3650)
    $Principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -RunLevel Highest
    $Settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

    Register-ScheduledTask -TaskName 'WireSockRefresh' -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings
    Write-Host 'WireSock Refresh task created successfully (direct batch execution, runs every 3 minutes)'
    exit 0
}} catch {{
    Write-Host 'Error: ' + $_.Exception.Message
    exit 1
}}";
                
                // PowerShell'i çalıştır
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas" // Yönetici izni ile çalıştır
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                File.AppendAllText(logPath, $"PowerShell çıktısı: {output}\n");
                if (!string.IsNullOrEmpty(error))
                {
                    File.AppendAllText(logPath, $"PowerShell hatası: {error}\n");
                }
                File.AppendAllText(logPath, $"PowerShell Exit Code: {process.ExitCode}\n");
                
                if (process.ExitCode == 0)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi başarıyla oluşturuldu.\n");
                    return true;
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: WireSock Refresh görevi oluşturulamadı.\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock Refresh görevi oluşturma hatası: {ex.Message}\n");
                return false;
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
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.5. Kurulum Exit Code: {process.ExitCode}, kurulum doğrulanıyor...\n");
                        
                        // Check if WireSock is actually installed despite the error
                        if (_wireSockService.IsWireSockInstalled())
                        {
                            File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2.2.3.6. Kurulum doğrulandı (Exit Code: {process.ExitCode})\n");
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
                                return existingSecureConnectPath;
                            }

                            // Mevcut kurulum yoksa yeni WireSock Secure Connect klasörünü kullan
                            var newInstallPath = Path.Combine(programFilesPath, "WireSock Secure Connect");
                            return newInstallPath;
                        }
                        else if (Directory.Exists(programFilesX86Path))
                        {
                            // Sadece mevcut WireSock Secure Connect kurulumunu kontrol et
                            var existingSecureConnectPath = Path.Combine(programFilesX86Path, "WireSock Secure Connect");
                            
                            if (Directory.Exists(existingSecureConnectPath))
                            {
                                return existingSecureConnectPath;
                            }

                            // Mevcut kurulum yoksa yeni WireSock Secure Connect klasörünü kullan
                            var newInstallPath = Path.Combine(programFilesX86Path, "WireSock Secure Connect");
                            return newInstallPath;
                        }
                        else
                        {
                            // Program Files klasörü yoksa oluştur
                            try
                            {
                                Directory.CreateDirectory(programFilesPath);
                                var newInstallPath = Path.Combine(programFilesPath, "WireSock Secure Connect");
                                return newInstallPath;
                            }
                            catch
                            {
                                var fallbackInstallPath = Path.Combine(drive.Name, "WireSock Secure Connect");
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
            if (TabControl.SelectedIndex == 1) // ByeDPI sekmesi
            {
                // ByeDPI sekmesi için pencere boyutunu ayarla
                // Önceki animasyonları durdur ve doğrudan boyut ayarla
                this.BeginAnimation(HeightProperty, null);
                
                // Merkezi boyut hesaplama ve güncelleme
                AnimateWindowHeight(_byeDPIHeight, TimeSpan.FromMilliseconds(400));
                
                this.Width = 500;
                
                // Cache'den hızlı kontrol yap
                CheckByeDPIRemoveButtonVisibilityFromCache();
                
                // ByeDPI UI durumunu güncelle
                UpdateByeDPIUIState();
            }
            else if (TabControl.SelectedIndex == 2) // Zapret sekmesi
            {
                // Zapret sekmesi açıldığında dosyaları kontrol et ve gerekirse kopyala
                CheckAndCopyZapretFilesIfNeeded();
                
                // Zapret sekmesi için pencere boyutunu ayarla
                // Önceki animasyonları durdur ve doğrudan boyut ayarla
                this.BeginAnimation(HeightProperty, null);
                
                // Merkezi boyut hesaplama ve güncelleme
                UpdateZapretWindowSize();
                
                this.Width = 500;
                
                // Cache'den hızlı kontrol yap
                CheckZapretRemoveButtonVisibilityFromCache();
            }
            else if (TabControl.SelectedIndex == 3) // GoodbyeDPI sekmesi
            {
                // GoodbyeDPI sekmesi açıldığında dosyaları kontrol et ve gerekirse kopyala
                CheckAndCopyGoodbyeDPIFilesIfNeeded();
                
                // GoodbyeDPI sekmesi için pencere boyutunu ayarla
                // Önceki animasyonları durdur ve doğrudan boyut ayarla
                this.BeginAnimation(HeightProperty, null);
                
                // Switch durumlarını kontrol et ve boyutu hesapla
                UpdateGoodbyeDPIWindowSize();
                
                this.Width = 500;
                
                // Cache'den hızlı kontrol yap
                CheckGoodbyeDPIRemoveButtonVisibilityFromCache();
            }
            else if (TabControl.SelectedIndex == 4) // Gelişmiş sekmesi
            {
                // Gelişmiş sekmesi için pencere boyutunu ayarla (yeni butonlar için artırıldı)
                // Önceki animasyonları durdur ve doğrudan boyut ayarla
                this.BeginAnimation(HeightProperty, null);
                
                // Merkezi boyut hesaplama ve güncelleme
                AnimateWindowHeight(_advancedHeight, TimeSpan.FromMilliseconds(400));
                
                    this.Width = 500;
                
                // Cache'den hızlı güncelleme yap
                if (_serviceStatusCache.Count > 0)
                {
                    UpdateAllServiceUIFromCache();
                }
                else
                {
                    // Cache boşsa asenkron olarak kontrol et
                    _ = Task.Run(async () => await CheckAllServicesAsync());
                }
            }
            else // Ana Sayfa sekmesi
            {
                // Ana Sayfa sekmesi için pencere boyutunu ayarla
                // Önceki animasyonları durdur ve doğrudan boyut ayarla
                this.BeginAnimation(HeightProperty, null);
                
                // Merkezi boyut hesaplama ve güncelleme
                UpdateMainPageWindowSize();
                
                this.Width = 500;
            }
            
            // Overlay görünürlüğünü sekmeye göre güncelle
            UpdateOverlayVisibilityForCurrentTab();
        }



        private async void CheckAndCopyGoodbyeDPIFilesIfNeeded()
        {
            try
            {
                // Eğer dosyalar zaten varsa, hiçbir şey yapma
                if (CheckGoodbyeDPIFilesExist())
                {
                    return;
                }

                // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                if (AreCriticalWinDivertFilesMissing())
                {
                    Debug.WriteLine("Kritik WinDivert dosyaları eksik - GoodbyeDPI dosyaları LocalAppData'ya kopyalanmıyor");
                    return;
                }

                // Loading overlay'i göster
                loadingOverlay.Visibility = Visibility.Visible;

                // Dosyaları kopyala
                var success = await EnsureGoodbyeDPIFilesExist();
                
                if (success)
                {
                    // Preset'leri yeniden yükle
                    LoadGoodbyeDPIPresets();
            }
            else
            {
                    System.Windows.MessageBox.Show("GoodbyeDPI dosyaları kopyalanamadı!", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"GoodbyeDPI dosyaları kopyalanırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // Loading overlay'i gizle
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnByeDPISetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI Split Tunneling kurulumunu başlatmak istediğinizden emin misiniz?", 
                "ByeDPI ST Kurulum", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                PerformByeDPISetup();
            }
        }

        private void BtnByeDPIDLLSetup_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI DLL kurulumunu başlatmak istediğinizden emin misiniz?", 
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

                // 1. Kurulum öncesi temizlik
                File.AppendAllText(logPath, "1. Kurulum öncesi temizlik yapılıyor...\n");
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (cleanupSuccess)
                {
                    File.AppendAllText(logPath, "Kurulum öncesi temizlik başarıyla tamamlandı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, "UYARI: Kurulum öncesi temizlik sırasında hata oluştu.\n");
                }

                // 2. Prerequisites kurulumları
                File.AppendAllText(logPath, "2. Prerequisites kurulumları başlatılıyor...\n");
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
                else
                {
                    // Başarılı kurulum sonrası kaldır butonunu güncelle
                    CheckByeDPIRemoveButtonVisibility();
                }

                // 9. Kurulum tamamlandı mesajı
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
                else
                {
                    // Restart yapılmayacaksa UI'ı güncelle
                    UpdateByeDPIUIState();
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
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
                
                // UI'ı güncelle (restart yapılsa da yapılmasa da)
                UpdateByeDPIUIState();
            }
        }

        private async void PerformByeDPIDLLSetup()
        {
            ShowLoading(true);
            
            try
            {
                var logPath = GetLogPath();
                File.WriteAllText(logPath, $"ByeDPI DLL Kurulum Başlangıç: {DateTime.Now}\n");

                // 1. Kurulum öncesi temizlik
                File.AppendAllText(logPath, "1. Kurulum öncesi temizlik yapılıyor...\n");
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (cleanupSuccess)
                {
                    File.AppendAllText(logPath, "Kurulum öncesi temizlik başarıyla tamamlandı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, "UYARI: Kurulum öncesi temizlik sırasında hata oluştu.\n");
                }

                // 2. Prerequisites kurulumları
                File.AppendAllText(logPath, "2. Prerequisites kurulumları başlatılıyor...\n");
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
                else
                {
                    // Başarılı kurulum sonrası kaldır butonunu güncelle
                    CheckByeDPIRemoveButtonVisibility();
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

                // 9. Kurulum tamamlandı mesajı
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
                else
                {
                    // Restart yapılmayacaksa UI'ı güncelle
                    UpdateByeDPIUIState();
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
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
                
                // UI'ı güncelle (restart yapılsa da yapılmasa da)
                UpdateByeDPIUIState();
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

        private string GetGoodbyeDPILogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "goodbyedpi.log");
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

        private string GetZapretLogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "zapret.log");
        }

        private string GetLocalAppDataZapretPath()
        {
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var zapretPath = Path.Combine(localAppDataPath, "SplitWire-Turkey", "Zapret");
            
            if (!Directory.Exists(zapretPath))
            {
                Directory.CreateDirectory(zapretPath);
            }
            
            return zapretPath;
        }

        private bool CheckZapretFilesExist()
        {
            try
            {
                var localZapretPath = GetLocalAppDataZapretPath();
                var zapretWinwsPath = Path.Combine(localZapretPath, "zapret-winws");
                
                // Temel klasör ve dosya kontrolü
                if (!Directory.Exists(zapretWinwsPath) || 
                    !File.Exists(Path.Combine(zapretWinwsPath, "presets.txt")))
                {
                    return false;
                }
                
                // arm64/WinDivert64.sys dosyasının varlığını kontrol et
                var arm64WinDivertPath = Path.Combine(localZapretPath, "arm64", "WinDivert64.sys");
                if (!File.Exists(arm64WinDivertPath))
                {
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                // Hata durumunda false döndür
                Debug.WriteLine($"CheckZapretFilesExist hatası: {ex.Message}");
                return false;
            }
        }

        private async void CheckAndCopyZapretFilesIfNeeded()
        {
            try
            {
                // Eğer dosyalar zaten varsa, hiçbir şey yapma
                if (CheckZapretFilesExist())
                {
                    return;
                }

                // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                if (AreCriticalWinDivertFilesMissing())
                {
                    Debug.WriteLine("Kritik WinDivert dosyaları eksik - Zapret dosyaları LocalAppData'ya kopyalanmıyor");
                    return;
                }

                // Loading overlay'i göster
                loadingOverlay.Visibility = Visibility.Visible;

                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret sekmesi açıldığında dosya kopyalama işlemi başlatılıyor...\n");

                // Dosyaları kopyala
                var success = await EnsureZapretFilesExist();
                
                if (success)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dosya kopyalama başarılı. Preset'ler yükleniyor...\n");
                    
                    // Preset'leri yeniden yükle
                    LoadZapretPresets();
                    
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Preset'ler başarıyla yüklendi.\n");
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dosya kopyalama başarısız!\n");
                }
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret sekmesi açıldığında hata: {ex.Message}\n");
                
                System.Windows.MessageBox.Show($"Zapret dosyaları kopyalanırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // Loading overlay'i gizle
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ContinuouslyHideZapretProcesses(CancellationToken cancellationToken, string zapretLogPath)
        {
            var hiddenProcesses = new HashSet<int>(); // Zaten gizlenmiş process'leri takip et
            var lastCheckTime = DateTime.Now;
            
            try
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hibrit process gizleme başlatıldı.\n");
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    var currentTime = DateTime.Now;
                    
                                         // Her 100ms'de bir kontrol (500ms yerine) - Kullanıcı konsol pencerelerini görmesin
                     if ((currentTime - lastCheckTime).TotalMilliseconds >= 100)
                     {
                         CheckAndHideNewProcesses(hiddenProcesses, zapretLogPath);
                         lastCheckTime = currentTime;
                     }
                    
                    // Kısa bekleme ile responsive ol
                    await Task.Delay(50, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation - beklenen durum
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process gizleme task'i iptal edildi.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hibrit process gizleme hatası: {ex.Message}\n");
            }
        }

        private void CheckAndHideNewProcesses(HashSet<int> hiddenProcesses, string zapretLogPath)
        {
            var processNames = new[] { "winws", "tee", "bash", "sh", "elevator", "cmd" };
            
            foreach (var processName in processNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Zaten gizlenmiş process'leri atla
                            if (hiddenProcesses.Contains(process.Id))
                                continue;
                            
                            // Process bitmiş ise listeden çıkar
                            if (process.HasExited)
                            {
                                hiddenProcesses.Remove(process.Id);
                                continue;
                            }
                            
                            // Process'in pencere handle'ı varsa gizle
                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                bool success = ShowWindow(process.MainWindowHandle, SW_HIDE);
                                if (success)
                                {
                                    hiddenProcesses.Add(process.Id);
                                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process gizlendi: {processName} (PID: {process.Id})\n");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Process gizleme hatası - devam et
                            if (!ex.Message.Contains("has exited"))
                            {
                                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process gizleme hatası ({processName}, PID: {process.Id}): {ex.Message}\n");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process arama hatası ({processName}): {ex.Message}\n");
                }
            }
        }

        private void HideZapretProcesses(int parentProcessId)
        {
            try
            {
                // Bu metod artık kullanılmıyor - ContinuouslyHideZapretProcesses kullan
                var processNames = new[] { "winws", "tee", "bash", "sh" };
                
                foreach (var processName in processNames)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Process'in parent'ını kontrol et (basit yaklaşım)
                            if (process.StartTime > DateTime.Now.AddSeconds(-10)) // Son 10 saniyede başlayan
                            {
                                // Windows API ile pencereyi gizle
                                HideProcessWindow(process);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Process gizleme hatası - devam et
                            System.Diagnostics.Debug.WriteLine($"Process gizleme hatası ({processName}): {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HideZapretProcesses hatası: {ex.Message}");
            }
        }

        private void HideProcessWindow(Process process)
        {
            try
            {
                // Process'in ana pencere handle'ını al
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    // Windows API ile pencereyi gizle
                    ShowWindow(process.MainWindowHandle, SW_HIDE);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HideProcessWindow hatası: {ex.Message}");
            }
        }

        // Windows API import'ları
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

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

        private async Task<bool> ResetModernDNSSettingsAsync()
        {
            var standardLogPath = GetStandardSetupLogPath();
            
            try
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları geri alınıyor...\n");
                
                // 60 saniye timeout ile DNS ayarlarını geri al
                var timeoutTask = Task.Run(() =>
                {
                    try
                    {
                        bool allCommandsSuccessful = true;
                        var logPath = GetDNSLogPath();
                        File.WriteAllText(logPath, $"DNS Geri Alma Başlangıç: {DateTime.Now}\n");

                        // PowerShell script ile DNS ayarlarını geri al
                        Debug.WriteLine("PowerShell ile DNS ayarları geri alınıyor...");
                        File.AppendAllText(logPath, "PowerShell ile DNS ayarları geri alınıyor...\n");

                        var psScript = @"
# Fiziksel ağ adaptörlerini al
$adapters = Get-NetAdapter -Physical

# Her adaptör için DNS ayarlarını geri al
foreach ($adapter in $adapters) {
    $adapterName = $adapter.Name
    $adapterGuid = $adapter.InterfaceGuid
    
    Write-Host ""Processing adapter: $adapterName""
    
    # IPv4 DNS ayarlarını otomatik yap
    try {
        Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -ResetServerAddresses
        Write-Host ""IPv4 DNS settings reset to automatic: $adapterName""
    }
    catch {
        Write-Host ""Failed to reset IPv4 DNS settings: $adapterName - $($_.Exception.Message)""
    }
    
    # DoH ayarlarını temizle
    $dohPath = 'HKLM:System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\' + $adapterGuid + '\DohInterfaceSettings'
    
    try {
        # Mevcut DoH ayarlarını tamamen temizle
        if (Test-Path $dohPath) {
            Remove-Item -Path $dohPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host ""DoH settings cleared: $adapterName""
        }
    }
    catch {
        Write-Host ""Failed to clear DoH settings: $adapterName - $($_.Exception.Message)""
    }
}

# Global DoH ayarlarını kapat - Registry üzerinden
try {
    # Registry'de DoH ayarlarını temizle
    $globalDohPath = 'HKLM:SOFTWARE\Policies\Microsoft\Windows NT\DNSClient'
    if (Test-Path $globalDohPath) {
        Remove-ItemProperty -Path $globalDohPath -Name 'DohEnabled' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $globalDohPath -Name 'DohServerAddress' -ErrorAction SilentlyContinue
    }
    Write-Host ""Global DoH settings disabled via registry""
}
catch {
    Write-Host ""Failed to disable global DoH settings: $($_.Exception.Message)""
}

# DNS önbelleğini temizle
Clear-DnsClientCache
Write-Host ""DNS settings reset completed""
";

                        var result = ExecutePowerShellScript(psScript);
                        
                        if (result == 0)
                        {
                            Debug.WriteLine("PowerShell DNS geri alma başarılı.");
                            File.AppendAllText(logPath, "PowerShell DNS geri alma başarılı.\n");
                            allCommandsSuccessful = true;
                        }
                        else
                        {
                            Debug.WriteLine($"PowerShell DNS geri alma başarısız. Exit Code: {result}");
                            File.AppendAllText(logPath, $"PowerShell DNS geri alma başarısız. Exit Code: {result}\n");
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

                        File.AppendAllText(logPath, $"DNS Geri Alma Bitiş: {DateTime.Now}\n");
                        return allCommandsSuccessful;
                    }
                    catch (Exception ex)
                    {
                        var logPath = GetDNSLogPath();
                        File.AppendAllText(logPath, $"DNS Geri Alma Hatası: {ex.Message}\n");
                        Debug.WriteLine($"DNS geri alma hatası: {ex.Message}");
                        return false;
                    }
                });

                // 60 saniye timeout ile bekle
                var timeout = TimeSpan.FromSeconds(60);
                var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(timeout));

                if (completedTask == timeoutTask)
                {
                    // DNS geri alma tamamlandı
                    var result = await timeoutTask;
                    if (result)
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları başarıyla geri alındı.\n");
                        return true;
                    }
                    else
                    {
                        File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları geri alınamadı.\n");
                        return false;
                    }
                }
                else
                {
                    // Timeout oluştu
                    File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS geri alma timeout (60 saniye).\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(standardLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: DNS geri alma sırasında hata oluştu: {ex.Message}\n");
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

                // Kontrol sonuçları - Sadece DoH ayarlarını kontrol et
                bool hasDoHSettings = dohOutput.Contains("DoHSettings") && !dohOutput.Contains("0");

                Debug.WriteLine($"DoH Ayarları: {hasDoHSettings}");
                File.AppendAllText(logPath, $"Doğrulama Sonuçları: DoH Ayarları={hasDoHSettings}\n");

                // DNS ayarları zaten başarıyla otomatik yapıldı, sadece DoH kontrolü yeterli
                return !hasDoHSettings; // DoH ayarları yoksa başarılı
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
        /// Tüm kurulum işlemleri öncesi temizlik yapar
        /// </summary>
        private async Task<bool> PerformPreSetupCleanupAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kurulum öncesi temizlik başlatılıyor...\n");
                    
                    // 1. Discord.exe'yi durdur
                    File.AppendAllText(logPath, "1. Discord.exe durduruluyor...\n");
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

                                // 2. Hizmetleri sırayla durdur ve kaldır
            var services = new[] { 
                "GoodbyeDPI", 
                "zapret", 
                "byedpi", 
                "winws1", 
                "winws2", 
                "wiresock-client-service", 
                "ProxiFyreService", 
                "WinDivert" 
            };
            
            File.AppendAllText(logPath, "2. Hizmetler durduruluyor ve kaldırılıyor...\n");
            
            foreach (var service in services)
            {
                File.AppendAllText(logPath, $"{service} hizmeti işleniyor...\n");
                
                try
                {
                    // Hizmeti durdur
                    var stopResult = ExecuteCommand("sc", $"stop {service}");
                    File.AppendAllText(logPath, $"{service} durdurma sonucu: {stopResult}\n");
                    
                    // Kısa bekleme
                    Thread.Sleep(1000);
                    
                    // Hizmeti kaldır
                    var removeResult = ExecuteCommand("sc", $"delete {service}");
                    File.AppendAllText(logPath, $"{service} kaldırma sonucu: {removeResult}\n");
                    
                    // Kısa bekleme
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"{service} işlenirken hata: {ex.Message}\n");
                }
            }

            // 3. Drover dosyalarını temizle
            File.AppendAllText(logPath, "3. Drover dosyaları temizleniyor...\n");
            try
            {
                var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                bool filesRemoved = false;

                // Discord app-* klasörlerinde drover dosyalarını ara ve sil
                if (Directory.Exists(discordPath))
                {
                    var appFolders = Directory.GetDirectories(discordPath, "app-*");
                    File.AppendAllText(logPath, $"{appFolders.Length} adet app-* klasörü bulundu.\n");
                    
                    foreach (var appFolder in appFolders)
                    {
                        var versionDllPath = Path.Combine(appFolder, "version.dll");
                        var droverIniPath = Path.Combine(appFolder, "drover.ini");

                        if (File.Exists(versionDllPath))
                        {
                            try
                            {
                                File.Delete(versionDllPath);
                                filesRemoved = true;
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
                                filesRemoved = true;
                                File.AppendAllText(logPath, $"drover.ini silindi: {droverIniPath}\n");
                            }
                            catch (Exception ex)
                            {
                                File.AppendAllText(logPath, $"drover.ini silinirken hata: {ex.Message}\n");
                            }
                        }
                    }

                    if (filesRemoved)
                    {
                        File.AppendAllText(logPath, "Drover dosyaları başarıyla temizlendi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "Drover dosyaları bulunamadı.\n");
                    }
                }
                else
                {
                    File.AppendAllText(logPath, "Discord klasörü bulunamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Drover dosyaları temizlenirken hata: {ex.Message}\n");
            }

            File.AppendAllText(logPath, "Kurulum öncesi temizlik tamamlandı.\n");
                    return true;
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"Kurulum öncesi temizlik hatası: {ex.Message}\n");
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

        #region Zapret Methods

        private void LoadZapretPresets()
        {
            try
            {
                var localZapretPath = GetLocalAppDataZapretPath();
                var presetsPath = Path.Combine(localZapretPath, "zapret-winws", "presets.txt");

                _zapretPresets.Clear();
                cmbZapretPresets.Items.Clear();

                if (File.Exists(presetsPath))
                {
                    var lines = File.ReadAllLines(presetsPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[i]))
                        {
                            var line = lines[i].Trim();
                            
                            // Yeni format: "İsim:Parametreler"
                            if (line.Contains(":"))
                            {
                                var parts = line.Split(new[] { ':' }, 2); // Sadece ilk : ile böl
                                if (parts.Length == 2)
                                {
                                    var presetName = parts[0].Trim();
                                    var presetParams = parts[1].Trim();
                                    
                                    _zapretPresets.Add(presetParams); // Parametreleri sakla
                                    cmbZapretPresets.Items.Add(presetName); // İsimleri göster
                                }
                            }
                            else
                            {
                                // Eski format desteği (geriye dönük uyumluluk)
                                _zapretPresets.Add(line);
                                cmbZapretPresets.Items.Add($"Önayar {i + 1}");
                            }
                        }
                    }
                }

                if (cmbZapretPresets.Items.Count > 0)
                {
                    cmbZapretPresets.SelectedIndex = 0;
                }
                else
                {
                    // Eğer preset yoksa, kullanıcıya bilgi ver
                    cmbZapretPresets.Items.Add("Zapret dosyaları bulunamadı");
                    cmbZapretPresets.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Zapret önayarları yüklenirken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<bool> EnsureZapretFilesExist()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                
                // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                if (AreCriticalWinDivertFilesMissing())
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: Kritik WinDivert dosyaları eksik olduğu için LocalAppData'ya kopyalama işlemi yapılmıyor.\n");
                    Debug.WriteLine("Kritik WinDivert dosyaları eksik - Zapret dosyaları LocalAppData'ya kopyalanmıyor");
                    return false;
                }
                
                var localZapretPath = GetLocalAppDataZapretPath();
                var sourceZapretPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "zapret");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret dosyaları kontrol ediliyor...\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kaynak klasör: {sourceZapretPath}\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hedef klasör: {localZapretPath}\n");
                
                // Kaynak klasör kontrolü
                if (!Directory.Exists(sourceZapretPath))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Kaynak klasör bulunamadı!\n");
                    System.Windows.MessageBox.Show($"Zapret kaynak klasörü bulunamadı: {sourceZapretPath}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Local klasörü her zaman yeniden oluştur (güncel dosyalar için)
                if (Directory.Exists(localZapretPath))
                {
                    try
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Mevcut local klasör siliniyor...\n");
                        Directory.Delete(localZapretPath, true);
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Local klasör silindi.\n");
            }
            catch (Exception ex)
            {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Local klasör silme hatası: {ex.Message}\n");
                        System.Diagnostics.Debug.WriteLine($"Local klasör silme hatası: {ex.Message}");
                    }
                }

                // Local klasörü oluştur
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Local klasör oluşturuluyor...\n");
                Directory.CreateDirectory(localZapretPath);

                // Dosyaları kopyala
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dosyalar kopyalanıyor...\n");
                await Task.Run(() => CopyDirectory(sourceZapretPath, localZapretPath));
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dosya kopyalama tamamlandı.\n");
                
                // Zapret Otomatik Kurulum için özel dosyaları kopyala
                var sourceHiddenCmdPath = Path.Combine(sourceZapretPath, "blockcheck", "blockcheck-hidden.cmd");
                var destHiddenCmdPath = Path.Combine(localZapretPath, "blockcheck", "blockcheck-hidden.cmd");
                var sourceHiddenShPath = Path.Combine(sourceZapretPath, "blockcheck", "zapret", "blog-hidden.sh");
                var destHiddenShPath = Path.Combine(localZapretPath, "blockcheck", "zapret", "blog-hidden.sh");
                
                // blockcheck-hidden.cmd kopyala
                if (File.Exists(sourceHiddenCmdPath))
        {
            try
            {
                        File.Copy(sourceHiddenCmdPath, destHiddenCmdPath, true);
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] blockcheck-hidden.cmd kopyalandı.\n");
            }
            catch (Exception ex)
            {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] blockcheck-hidden.cmd kopyalama hatası: {ex.Message}\n");
            }
        }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: blockcheck-hidden.cmd kaynak dosyası bulunamadı.\n");
        }

                // blog-hidden.sh kopyala
                if (File.Exists(sourceHiddenShPath))
        {
            try
            {
                        File.Copy(sourceHiddenShPath, destHiddenShPath, true);
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] blog-hidden.sh kopyalandı.\n");
            }
            catch (Exception ex)
            {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] blog-hidden.sh kopyalama hatası: {ex.Message}\n");
                    }
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: blog-hidden.sh kaynak dosyası bulunamadı.\n");
                }
                
                // Kopyalama sonrası kontrol
                if (!Directory.Exists(localZapretPath))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Local klasör oluşturulamadı!\n");
                    System.Windows.MessageBox.Show("Zapret dosyaları kopyalanamadı!", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Önemli dosyaların varlığını kontrol et
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Gerekli dosyalar kontrol ediliyor...\n");
                var requiredFiles = new[]
                {
                    Path.Combine(localZapretPath, "blockcheck", "blockcheck.cmd"),
                };

                foreach (var file in requiredFiles)
                {
                    if (!File.Exists(file))
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Gerekli dosya bulunamadı: {file}\n");
                        System.Windows.MessageBox.Show($"Gerekli dosya bulunamadı: {file}", 
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                    else
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dosya bulundu: {Path.GetFileName(file)}\n");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Zapret dosyaları kopyalanırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            try
            {
                // Hedef klasör varsa önce sil
                if (Directory.Exists(destinationDir))
                {
                    Directory.Delete(destinationDir, true);
                }

                // Yeni klasör oluştur
                Directory.CreateDirectory(destinationDir);

                // Dosyaları kopyala
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(destinationDir, fileName);
                        File.Copy(file, destFile, true);
            }
            catch (Exception ex)
            {
                        System.Diagnostics.Debug.WriteLine($"Dosya kopyalama hatası: {file} -> {ex.Message}");
            }
        }

                // Alt klasörleri kopyala
                foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            try
            {
                        string dirName = Path.GetFileName(subDir);
                        string destSubDir = Path.Combine(destinationDir, dirName);
                        CopyDirectory(subDir, destSubDir);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Alt klasör kopyalama hatası: {subDir} -> {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Klasör kopyalama genel hatası: {ex.Message}");
                throw;
            }
        }

        private void CmbZapretPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbZapretPresets.SelectedIndex >= 0 && cmbZapretPresets.SelectedIndex < _zapretPresets.Count)
            {
                // Önayar değiştiğinde textbox içeriğini güncelle
                txtZapretParams.Text = _zapretPresets[cmbZapretPresets.SelectedIndex];
                
                // Eğer manuel parametre girişi aktifse, kullanıcıya bilgi ver
                if (chkManualParams.IsChecked == true)
                {
                    // Textbox'ı görünür yap (eğer gizliyse)
            txtZapretParams.Visibility = Visibility.Visible;
                }
            }
        }

        private void ChkManualParams_Checked(object sender, RoutedEventArgs e)
        {
            txtZapretParams.Visibility = Visibility.Visible;
            _zapretManualParamsActive = true;
            
            // Manuel parametre girişi aktif edildiğinde, mevcut seçili önayarı textbox'a yükle
            if (cmbZapretPresets.SelectedIndex >= 0 && cmbZapretPresets.SelectedIndex < _zapretPresets.Count)
            {
                txtZapretParams.Text = _zapretPresets[cmbZapretPresets.SelectedIndex];
            }
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateZapretWindowSize();
        }

        private void ChkManualParams_Unchecked(object sender, RoutedEventArgs e)
        {
            txtZapretParams.Visibility = Visibility.Collapsed;
            _zapretManualParamsActive = false;
            
            // Manuel parametre girişi kapatıldığında, seçili önayarı textbox'a yükle
            if (cmbZapretPresets.SelectedIndex >= 0 && cmbZapretPresets.SelectedIndex < _zapretPresets.Count)
            {
                txtZapretParams.Text = _zapretPresets[cmbZapretPresets.SelectedIndex];
            }
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateZapretWindowSize();
        }

        private void ChkAdvancedSettings_Checked(object sender, RoutedEventArgs e)
        {
            // Gelişmiş ayarlar panelini görünür yap
            advancedSettingsPanel.Visibility = Visibility.Visible;
            _mainPageAdvancedSettingsActive = true;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateMainPageWindowSize();
        }

        private void ChkAdvancedSettings_Unchecked(object sender, RoutedEventArgs e)
        {
            // Gelişmiş ayarlar panelini gizle
            advancedSettingsPanel.Visibility = Visibility.Collapsed;
            _mainPageAdvancedSettingsActive = false;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateMainPageWindowSize();
        }

        private async void BtnRemoveByeDPI_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "ByeDPI hizmetlerini kaldırmak istediğinizden emin misiniz?",
                "ByeDPI Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ShowLoading(true);

                // 1. ByeDPI hizmetini durdur ve kaldır
                if (IsServiceInstalled("ByeDPI"))
                {
                    ExecuteCommand("sc", "stop ByeDPI");
                    await Task.Delay(2000);
                    ExecuteCommand("sc", "delete ByeDPI");
                    await Task.Delay(1000);
                }

                // 2. ProxiFyreService hizmetini durdur ve kaldır
                if (IsServiceInstalled("ProxiFyreService"))
                {
                    ExecuteCommand("sc", "stop ProxiFyreService");
                    await Task.Delay(2000);
                    ExecuteCommand("sc", "delete ProxiFyreService");
                    await Task.Delay(1000);
                }

                // 3. Discord klasöründeki drover dosyalarını temizle
                await CleanupDroverFilesAsync();

                // 4. Discord.exe'yi durdur
                var discordStopSuccess = await StopDiscordProcessAsync();
                if (discordStopSuccess)
                {
                    // Kısa bir bekleme süresi ekle
                    await Task.Delay(2000);

                    var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                    bool filesRemoved = false;

                    // Discord app-* klasörlerinde drover dosyalarını ara ve sil
                    if (Directory.Exists(discordPath))
                    {
                        var appFolders = Directory.GetDirectories(discordPath, "app-*");
                        foreach (var appFolder in appFolders)
                        {
                            var versionDllPath = Path.Combine(appFolder, "version.dll");
                            var droverIniPath = Path.Combine(appFolder, "drover.ini");

                            if (File.Exists(versionDllPath))
                            {
                                try
                                {
                                    File.Delete(versionDllPath);
                                    filesRemoved = true;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"version.dll silinirken hata: {ex.Message}");
                                }
                            }

                            if (File.Exists(droverIniPath))
                            {
                                try
                                {
                                    File.Delete(droverIniPath);
                                    filesRemoved = true;
                }
                catch (Exception ex)
                {
                                    Debug.WriteLine($"drover.ini silinirken hata: {ex.Message}");
                                }
                            }
                        }
                    }

                    if (filesRemoved)
                    {
                        Debug.WriteLine("Drover dosyaları başarıyla kaldırıldı.");
                    }
                }

                // Başarı mesajı göster
                var messageResult = System.Windows.MessageBox.Show(
                    "ByeDPI hizmetleri ve drover dosyaları başarıyla kaldırıldı.",
                    "Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Kullanıcı "Tamam" dedikten sonra hizmet durumlarını güncelle
                if (messageResult == MessageBoxResult.OK)
                {
                    ShowLoading(false);
                    UpdateByeDPIUIState();
                    // Hizmet durumlarını güncelle
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateRemovedServiceStatus("ByeDPI");
                        UpdateRemovedServiceStatus("ProxiFyreService");
                    });

                    // Tüm hizmet durumlarını yenile
                    await ForceRefreshAllServicesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"ByeDPI kaldırma sırasında hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
                UpdateByeDPIUIState();
            }
        }

        // Yardım Butonları Event Handler'ları
        private void BtnHelpMainPage_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "Ana Sayfa Yardımı - SplitWire-Turkey",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            var titleText = new TextBlock
            {
                Text = "Ana Sayfa Kullanımı",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // RichTextBox kullanarak formatlı metin oluştur
            var helpText = new System.Windows.Controls.RichTextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                VerticalAlignment = VerticalAlignment.Top,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Metin içeriğini oluştur
            var paragraph = new Paragraph();
            
            // Not 1 - Başlığın hemen altına taşındı
            var note1 = new Run("Not: ")
            {
                FontWeight = FontWeights.Bold
            };
            var note1Text = new Run("Bu bölümdeki kurulumlar, yalnızca Discord uygulaması için (Eğer tarayıcı tünellemesini aktifleştirdiyseniz tarayıcılar da dahil) çalışır. Bu kurulumları gerçekleştirdikten sonra sisteminizi her yeniden başlatışınızda ilgili yöntem otomatik olarak çalışmaya başlar.");
            paragraph.Inlines.Add(note1);
            paragraph.Inlines.Add(note1Text);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());
            
            // Standart Kurulum
            var standardTitle = new Run("Standart Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var standardText = new Run("Wgcf ve WireSock 2.4.16.1 araçlarını kullanarak yalnızca Discord için tünelleme gerçekleştirir. (Tarayıcılar için de tünelleme yap seçeneği açık ise internet tarayıcılarında da tünelleme yapılır)");
            paragraph.Inlines.Add(standardTitle);
            paragraph.Inlines.Add(standardText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Alternatif Kurulum
            var alternativeTitle = new Run("Alternatif Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var alternativeText = new Run("Wgcf ve WireSock 1.4.7.1 araçlarını kullanarak YALNIZCA Discord için tünelleme gerçekleştirilir. (Tarayıcılar için de tünelleme yap seçeneği açık ise internet tarayıcılarında da tünelleme yapılır)");
            paragraph.Inlines.Add(alternativeTitle);
            paragraph.Inlines.Add(alternativeText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Tarayıcılar için tünelleme
            var browserTitle = new Run("Tarayıcılar için de tünelleme yap: ")
            {
                FontWeight = FontWeights.Bold
            };
            var browserText = new Run("Discord uygulaması yanında; Chrome, Firefox, Opera, OperaGX, Brave, Vivaldi ve Edge gibi popüler internet tarayıcıları için de tünelleme yapılır.");
            paragraph.Inlines.Add(browserTitle);
            paragraph.Inlines.Add(browserText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Klasör listesi özelleştirme
            var folderTitle = new Run("Klasör listesini özelleştir: ")
            {
                FontWeight = FontWeights.Bold
            };
            var folderText = new Run("Discord haricinde bir uygulama için tünelleme yapmak isterseniz bu bölümü kullanabilirsiniz.");
            paragraph.Inlines.Add(folderTitle);
            paragraph.Inlines.Add(folderText);
            paragraph.Inlines.Add(new LineBreak());

            // Alt başlıklar
            var subTitle1 = new Run("    Klasör Ekle: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText1 = new Run("Tünelleyeceğiniz uygulamanın bulunduğu klasörü seçerek listeye ekler.");
            paragraph.Inlines.Add(subTitle1);
            paragraph.Inlines.Add(subText1);
            paragraph.Inlines.Add(new LineBreak());

            var subTitle2 = new Run("    Listeyi Temizle: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText2 = new Run("Klasör listesini temizler.");
            paragraph.Inlines.Add(subTitle2);
            paragraph.Inlines.Add(subText2);
            paragraph.Inlines.Add(new LineBreak());

            var subTitle3 = new Run("    Özel Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText3 = new Run("Hazırladığın klasör listesi için Wgcf ve WireSock kullanarak kurulum yapar.");
            paragraph.Inlines.Add(subTitle3);
            paragraph.Inlines.Add(subText3);
            paragraph.Inlines.Add(new LineBreak());

            var subTitle4 = new Run("    Özel Config Oluştur: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText4 = new Run("Hazırladığınız klasör listesi için konfigürasyon dosyası oluşturur.");
            paragraph.Inlines.Add(subTitle4);
            paragraph.Inlines.Add(subText4);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Çıkış
            var exitTitle = new Run("Çıkış: ")
            {
                FontWeight = FontWeights.Bold
            };
            var exitText = new Run("Programı kapatır.");
            paragraph.Inlines.Add(exitTitle);
            paragraph.Inlines.Add(exitText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Not 2
            var note2 = new Run("Not 2: ")
            {
                FontWeight = FontWeights.Bold
            };
            var note2Text = new Run("Eğer Discord uygulaması Checking for updates… ekranında kalırsa modeminizi kapatıp 15 saniye bekledikten sonra tekrar açın ve ardından bilgisayarınızı yeniden başlatın.");
            paragraph.Inlines.Add(note2);
            paragraph.Inlines.Add(note2Text);

            helpText.Document = new FlowDocument(paragraph);
            
            // RichTextBox tema renklerini ayarla
            if (isDarkMode)
            {
                helpText.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                helpText.Foreground = System.Windows.Media.Brushes.Black;
            }
            
            contentStack.Children.Add(helpText);

            scrollViewer.Content = contentStack;
            mainGrid.Children.Add(scrollViewer);
            Grid.SetRow(scrollViewer, 1);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
        }

        private void BtnHelpByeDPI_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "ByeDPI Yardımı - SplitWire-Turkey",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            var titleText = new TextBlock
            {
                Text = "ByeDPI Kullanımı",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // RichTextBox kullanarak formatlı metin oluştur
            var helpText = new System.Windows.Controls.RichTextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                VerticalAlignment = VerticalAlignment.Top,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Metin içeriğini oluştur
            var paragraph = new Paragraph();
            
            // Not - Başlığın hemen altına taşındı
            var noteTitle = new Run("Not: ")
            {
                FontWeight = FontWeights.Bold
            };
            var noteText = new Run("Bu bölümdeki kurulumlar, yalnızca Discord uygulaması için (Eğer tarayıcı tünellemesini aktifleştirdiyseniz tarayıcılar da dahil) çalışır. Bu kurulumları gerçekleştirdikten sonra sisteminizi her yeniden başlatışınızda ilgili yöntem otomatik olarak çalışmaya başlar.");
            paragraph.Inlines.Add(noteTitle);
            paragraph.Inlines.Add(noteText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());
            
            // ByeDPI Split Tunneling Kurulum
            var splitTitle = new Run("ByeDPI Split Tunneling Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var splitText = new Run("ByeDPI ve ProxiFyre araçlarını kullanarak YALNIZCA Discord uygulaması için DPI aşımı gerçekleştirilir. (Tarayıcılar için de tünelleme yap seçeneği açık ise internet tarayıcılarında da DPI aşımı yapılır)");
            paragraph.Inlines.Add(splitTitle);
            paragraph.Inlines.Add(splitText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Tarayıcılar için tünelleme
            var browserTitle = new Run("Tarayıcılar için de tünelleme yap: ")
            {
                FontWeight = FontWeights.Bold
            };
            var browserText = new Run("Discord uygulaması yanında; Chrome, Firefox, Opera, OperaGX, Brave, Vivaldi ve Edge gibi popüler internet tarayıcıları için de DPI aşımı yapılır. Tarayıcılar için tünelleme seçeneğini değiştirip tekrar kurulum yapmak için önce ByeDPI'ı Kaldır butonuna tıklayarak ByeDPI'ı kaldırmalısınız.");
            paragraph.Inlines.Add(browserTitle);
            paragraph.Inlines.Add(browserText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // ByeDPI DLL Kurulum
            var dllTitle = new Run("ByeDPI DLL Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var dllText = new Run("ByeDPI ve drover (DLL hijacking yöntemi) kullanılarak YALNIZCA Discord uygulaması için DPI aşımı gerçekleştirilir. Bu yöntem yalnızca Discord uygulaması için çalışır, tarayıcılar veya diğer programlar için çalışmaz.");
            paragraph.Inlines.Add(dllTitle);
            paragraph.Inlines.Add(dllText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // ByeDPI'ı Kaldır
            var removeTitle = new Run("ByeDPI'ı Kaldır: ")
            {
                FontWeight = FontWeights.Bold
            };
            var removeText = new Run("ByeDPI'ı kaldırıp drover dosyalarını siler.");
            paragraph.Inlines.Add(removeTitle);
            paragraph.Inlines.Add(removeText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Not 2
            var note2 = new Run("Not 2: ")
            {
                FontWeight = FontWeights.Bold
            };
            var note2Text = new Run("Eğer Discord uygulaması Checking for updates… ekranında kalırsa modeminizi kapatıp 15 saniye bekledikten sonra tekrar açın ve ardından bilgisayarınızı yeniden başlatın.");
            paragraph.Inlines.Add(note2);
            paragraph.Inlines.Add(note2Text);

            helpText.Document = new FlowDocument(paragraph);
            
            // RichTextBox tema renklerini ayarla
            if (isDarkMode)
            {
                helpText.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                helpText.Foreground = System.Windows.Media.Brushes.Black;
            }
            
            contentStack.Children.Add(helpText);

            scrollViewer.Content = contentStack;
            mainGrid.Children.Add(scrollViewer);
            Grid.SetRow(scrollViewer, 1);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
        }

        private void BtnHelpZapret_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "Zapret Yardımı - SplitWire-Turkey",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            var titleText = new TextBlock
            {
                Text = "Zapret Kullanımı",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // RichTextBox kullanarak formatlı metin oluştur
            var helpText = new System.Windows.Controls.RichTextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                VerticalAlignment = VerticalAlignment.Top,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Metin içeriğini oluştur
            var paragraph = new Paragraph();
            
            // Not - Başlığın hemen altına taşındı
            var noteTitle = new Run("Not: ")
            {
                FontWeight = FontWeights.Bold
            };
            var noteText = new Run("Bu bölümdeki kurulumlar, sistem geneli çalışır. Hız kaybına sebep olmasa da bazı web site ve uygulamalarda bağlantı sorunlarına yol açabilir. Bu kurulumları gerçekleştirdikten sonra sisteminizi her yeniden başlatışınızda ilgili yöntem otomatik olarak çalışmaya başlar.");
            paragraph.Inlines.Add(noteTitle);
            paragraph.Inlines.Add(noteText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Zapret Otomatik Kurulum
            var autoTitle = new Run("Zapret Otomatik Kurulum: ")
            {
                FontWeight = FontWeights.Bold
            };
            var autoText = new Run("Zapret'in blockcheck isimli strateji bulma yazılımı ile sisteminiz ve internet servis sağlayıcınız için ideal parametreler bulunur ve bu parametreler ile Zapret kurulumu yapılarak DPI aşımı sağlanır.");
            paragraph.Inlines.Add(autoTitle);
            paragraph.Inlines.Add(autoText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Tarama
            var scanTitle = new Run("Tarama: ")
            {
                FontWeight = FontWeights.Bold
            };
            var scanText = new Run("İdeal parametreleri bulmak için gerçekleştirilen taramanın hızını seçer.");
            paragraph.Inlines.Add(scanTitle);
            paragraph.Inlines.Add(scanText);
            paragraph.Inlines.Add(new LineBreak());

            // Alt başlıklar
            var subTitle1 = new Run("    Hızlı: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText1 = new Run("2-10 dakika arası sürebilir.");
            paragraph.Inlines.Add(subTitle1);
            paragraph.Inlines.Add(subText1);
            paragraph.Inlines.Add(new LineBreak());

            var subTitle2 = new Run("    Standart: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText2 = new Run("5-30 dakika arası sürebilir.");
            paragraph.Inlines.Add(subTitle2);
            paragraph.Inlines.Add(subText2);
            paragraph.Inlines.Add(new LineBreak());

            var subTitle3 = new Run("    Tam: ")
            {
                FontWeight = FontWeights.Bold
            };
            var subText3 = new Run("10-50 dakika arası sürebilir.");
            paragraph.Inlines.Add(subTitle3);
            paragraph.Inlines.Add(subText3);
            paragraph.Inlines.Add(new LineBreak());

            var scanNote = new Run("Bu süreler tahmini sürelerdir. Sisteminize ve internet sağlayıcınızın paket inceleme politikalarına göre değişiklik gösterebilir.");
            paragraph.Inlines.Add(scanNote);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Hazır Ayar
            var presetTitle = new Run("Hazır Ayar: ")
            {
                FontWeight = FontWeights.Bold
            };
            var presetText = new Run("Zapret için önceden belirlenmiş parametrelerden birini seçer. (Bal Porsuğu'na hazır ayarlar için teşekkürler)");
            paragraph.Inlines.Add(presetTitle);
            paragraph.Inlines.Add(presetText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Hazır Ayarı Düzenle
            var editTitle = new Run("Hazır Ayarı Düzenle: ")
            {
                FontWeight = FontWeights.Bold
            };
            var editText = new Run("Seçtiğiniz hazır ayar üzerinde ince ayar ya da değişiklik yapmanızı sağlayan metin kutusunu açar. Bu kutuda düzenleme yaptıktan sonra aşağıdaki butonları kullanarak kutudaki parametreler ile kurulum sağlayabilir ya da tek seferlik çalıştırabilirsiniz.");
            paragraph.Inlines.Add(editTitle);
            paragraph.Inlines.Add(editText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Önayarlı Hizmet Kur
            var serviceTitle = new Run("Önayarlı Hizmet Kur: ")
            {
                FontWeight = FontWeights.Bold
            };
            var serviceText = new Run("Seçtiğiniz hazır ayar ile (Ya da düzenleme yaptıysanız düzenlenmiş hali ile) Zapret hizmetini kurar.");
            paragraph.Inlines.Add(serviceTitle);
            paragraph.Inlines.Add(serviceText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Önayarlı Tek Seferlik
            var onceTitle = new Run("Önayarlı Tek Seferlik: ")
            {
                FontWeight = FontWeights.Bold
            };
            var onceText = new Run("Seçtiğiniz hazır ayar ile (Ya da düzenleme yaptıysanız düzenlenmiş hali ile) Zapret'i tek seferlik çalıştırır. Açılan konsol penceresini kapattığınızda Zapret çalışmayı durdurur.");
            paragraph.Inlines.Add(onceTitle);
            paragraph.Inlines.Add(onceText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Zapret'i Kaldır
            var removeTitle = new Run("Zapret'i Kaldır: ")
            {
                FontWeight = FontWeights.Bold
            };
            var removeText = new Run("Zapret'i kaldırır.");
            paragraph.Inlines.Add(removeTitle);
            paragraph.Inlines.Add(removeText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Not 2
            var note2Title = new Run("Not 2: ")
            {
                FontWeight = FontWeights.Bold
            };
            var note2Text = new Run("Eğer Discord uygulaması Checking for updates… ekranında kalırsa modeminizi kapatıp 15 saniye bekledikten sonra tekrar açın ve ardından bilgisayarınızı yeniden başlatın.");
            paragraph.Inlines.Add(note2Title);
            paragraph.Inlines.Add(note2Text);

            helpText.Document = new FlowDocument(paragraph);
            
            // RichTextBox tema renklerini ayarla
            if (isDarkMode)
            {
                helpText.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                helpText.Foreground = System.Windows.Media.Brushes.Black;
            }
            
            contentStack.Children.Add(helpText);

            scrollViewer.Content = contentStack;
            mainGrid.Children.Add(scrollViewer);
            Grid.SetRow(scrollViewer, 1);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
        }

        private void BtnHelpAdvanced_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "Gelişmiş Sekmesi Yardımı - SplitWire-Turkey",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            var titleText = new TextBlock
            {
                Text = "Gelişmiş Kullanımı",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // RichTextBox kullanarak formatlı metin oluştur
            var helpText = new System.Windows.Controls.RichTextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                VerticalAlignment = VerticalAlignment.Top,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Metin içeriğini oluştur
            var paragraph = new Paragraph();
            
            // Hizmetler
            var servicesTitle = new Run("Hizmetler: ")
            {
                FontWeight = FontWeights.Bold
            };
            var servicesText = new Run("SplitWire-Turkey'in kurduğu ya da kullanıcının kurduğu DPI aşma ve tünelleme ile ilgili hizmetlerin listesini gösterir.");
            paragraph.Inlines.Add(servicesTitle);
            paragraph.Inlines.Add(servicesText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Tüm Hizmetleri Kaldır
            var removeAllTitle = new Run("Tüm Hizmetleri Kaldır: ")
            {
                FontWeight = FontWeights.Bold
            };
            var removeAllText = new Run("Listedeki tüm hizmetleri doğru sıra ile kaldırır, Discord klasöründe drover dosyalarını siler ve WireSock Refresh Task Scheduler görevini kaldırır.");
            paragraph.Inlines.Add(removeAllTitle);
            paragraph.Inlines.Add(removeAllText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // DNS ve DoH Ayarlarını Geri Al
            var dnsTitle = new Run("DNS ve DoH Ayarlarını Geri Al: ")
            {
                FontWeight = FontWeights.Bold
            };
            var dnsText = new Run("SplitWire-Turkey içerisinde bulunan herhangi bir kurulum gerçekleştirildiğinde yapılan DNS ve DoH ayarlarını sıfırlayarak DNS ayarını \"Otomatik (DHCP)\" ve DoH ayarını \"Kapalı\" hale getirir.");
            paragraph.Inlines.Add(dnsTitle);
            paragraph.Inlines.Add(dnsText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // SplitWire-Turkey'i Kaldır
            var uninstallTitle = new Run("SplitWire-Turkey'i Kaldır: ")
            {
                FontWeight = FontWeights.Bold
            };
            var uninstallText = new Run("SplitWire-Turkey'in yaptığı tüm değişiklikleri geri alıp sisteminizi eski hale getirdikten sonra SplitWire-Turkey'i kaldırma aracını başlatır.");
            paragraph.Inlines.Add(uninstallTitle);
            paragraph.Inlines.Add(uninstallText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Not
            var noteTitle = new Run("Not: ")
            {
                FontWeight = FontWeights.Bold
            };
            var noteText = new Run("WinDivert hizmeti, Zapret ya da GoodbyeDPI hizmetleri durdurulmadan kaldırılamaz. Bu sebeple birden fazla onay istenebilir.");
            paragraph.Inlines.Add(noteTitle);
            paragraph.Inlines.Add(noteText);

            helpText.Document = new FlowDocument(paragraph);
            
            // RichTextBox tema renklerini ayarla
            if (isDarkMode)
            {
                helpText.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                helpText.Foreground = System.Windows.Media.Brushes.Black;
            }
            
            contentStack.Children.Add(helpText);

            scrollViewer.Content = contentStack;
            mainGrid.Children.Add(scrollViewer);
            Grid.SetRow(scrollViewer, 1);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
        }

        private async void BtnZapretAutoInstall_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Zapret Otomatik Kurulum başlatmak istediğinizden emin misiniz? Bu işlem tercih ettiğiniz tarama hızına göre 2-50 dakika sürebilir.",
                "Zapret Otomatik Kurulum",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                loadingOverlay.Visibility = Visibility.Visible;
                
                try
                {
                    var zapretLogPath = GetZapretLogPath();
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret otomatik kurulum başlatılıyor...\n");
                    
                    // Önce hizmetleri durdur ve kaldır
                    await PerformServiceCleanupForZapret();
                    
                    // Dosyalar yoksa kopyala
                    if (!CheckZapretFilesExist())
                    {
                        // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                        if (AreCriticalWinDivertFilesMissing())
                        {
                            System.Windows.MessageBox.Show("Kritik WinDivert dosyaları eksik olduğu için Zapret kurulumu yapılamıyor. Lütfen önce gerekli dosyaları ekleyin.", 
                                "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        
                        if (!await EnsureZapretFilesExist())
                        {
                            return;
                        }
                    }

                    // Son olarak Zapret kurulumunu gerçekleştir
                    await PerformZapretInstallationProcess();
            }
            catch (Exception ex)
            {
                    System.Windows.MessageBox.Show($"Zapret kurulumu sırasında hata oluştu: {ex.Message}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    loadingOverlay.Visibility = Visibility.Collapsed;
                    
                    // Hizmet durumlarını güncelle
                    CheckAllServices();
                }
            }
        }

        private async Task PerformServiceCleanupForZapret()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret için hizmet temizliği başlatılıyor...\n");

                // Kurulum öncesi temizlik (Discord + tüm hizmetler)
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kurulum öncesi temizlik yapılıyor...\n");
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (cleanupSuccess)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kurulum öncesi temizlik başarıyla tamamlandı.\n");
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: Kurulum öncesi temizlik sırasında hata oluştu.\n");
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet temizliği tamamlandı.\n");
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet temizliği hatası: {ex.Message}\n");
                throw new Exception($"Hizmet temizliği başarısız: {ex.Message}", ex);
            }
        }

        private async Task PerformZapretInstallationProcess()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulum süreci başlatılıyor...\n");

                // DNS ayarları
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları yapılıyor...\n");
                await SetModernDNSSettingsAsync();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları tamamlandı.\n");

                // Zapret kurulumunu gerçekleştir
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulumu başlatılıyor...\n");
                await RunZapretInstallation();
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulum süreci başarıyla tamamlandı.\n");
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulum süreci hatası: {ex.Message}\n");
                throw new Exception($"Zapret kurulum süreci başarısız: {ex.Message}", ex);
            }
        }



        private async Task StopDiscordProcesses()
        {
            try
            {
                var discordProcesses = Process.GetProcessesByName("Discord");
                foreach (var process in discordProcesses)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch
                    {
                        // İşlem zaten sonlanmış olabilir
                    }
                }
            }
            catch
            {
                // Discord çalışmıyor olabilir
            }
        }



        private async Task RunZapretInstallation()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulum süreci başlatılıyor...\n");
                
                var localZapretPath = GetLocalAppDataZapretPath();
                var blockcheckShPath = Path.Combine(localZapretPath, "blockcheck", "zapret", "blockcheck.sh");
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Local Zapret yolu: {localZapretPath}\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck.sh yolu: {blockcheckShPath}\n");
                
                // SCANLEVEL ayarını güncelle
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCANLEVEL ayarı güncelleniyor...\n");
                await UpdateScanLevel(blockcheckShPath);
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCANLEVEL ayarı güncellendi.\n");
                
                // Zapret Otomatik Kurulum için özel blockcheck-hidden.cmd dosyasını kullan
                var blockcheckCmdPath = Path.Combine(localZapretPath, "blockcheck", "blockcheck-hidden.cmd");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck-hidden.cmd çalıştırılıyor: {blockcheckCmdPath}\n");
                
                // Blockcheck process'ini çalıştır ve bash.exe'nin kapanmasını bekle
                await Task.Run(async () =>
                {
                    // Zapret işlemlerini gizli olarak başlatmak için özel ProcessStartInfo
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{blockcheckCmdPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Path.GetDirectoryName(blockcheckCmdPath),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true
                    };
                    
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process başlatılıyor...\n");
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process başlatıldı. PID: {process.Id}\n");
                        
                        // Process ID'yi al
                        var processId = process.Id;
                        
                                        // Process gizleme task'ini başlat
                var cancellationTokenSource = new CancellationTokenSource();
                var hideProcessTask = Task.Run(() => ContinuouslyHideZapretProcesses(cancellationTokenSource.Token, zapretLogPath));
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process gizleme task'i başlatıldı.\n");
                        
                        // Ana process'in kapanmasını bekle
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ana process kapanması bekleniyor...\n");
                        process.WaitForExit();
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ana process kapandı. Exit Code: {process.ExitCode}\n");
                        
                        // Bash.exe process'lerini bul ve kapanmasını bekle
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bash process'leri aranıyor...\n");
                        var bashProcesses = Process.GetProcessesByName("bash");
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {bashProcesses.Length} adet bash process bulundu.\n");
                        
                        foreach (var bashProcess in bashProcesses)
                        {
                            try
                            {
                                // Sadece bizim process'ten sonra başlayan bash process'lerini bekle
                                if (bashProcess.StartTime > process.StartTime)
                                {
                                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bash process bekleniyor. PID: {bashProcess.Id}\n");
                                    bashProcess.WaitForExit();
                                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bash process kapandı. PID: {bashProcess.Id}\n");
                                }
                            }
                            catch (Exception ex)
                            {
                                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bash process bekleme hatası: {ex.Message}\n");
                            }
                        }
                        
                        // Ek olarak, blockcheck.log dosyasının oluşmasını bekle
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck.log dosyası bekleniyor...\n");
                        var logPath = Path.Combine(localZapretPath, "blockcheck", "blockcheck.log");
                        var maxWaitTime = TimeSpan.FromMinutes(30); // Maksimum 30 dakika bekle
                        var startTime = DateTime.Now;
                        
                        while (!File.Exists(logPath) && (DateTime.Now - startTime) < maxWaitTime)
                        {
                            Thread.Sleep(1000); // 1 saniye bekle
                        }
                        
                        if (!File.Exists(logPath))
                        {
                            var errorMsg = "blockcheck.log dosyası oluşturulamadı (30 dakika timeout)";
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {errorMsg}\n");
                            throw new Exception(errorMsg);
                        }
                        
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck.log dosyası bulundu: {logPath}\n");
                        
                        // Process gizleme task'ini durdur
                        cancellationTokenSource.Cancel();
                        try
                        {
                            await hideProcessTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Normal cancellation - beklenen durum
                        }
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process gizleme task'i durduruldu.\n");
                    }
                });

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck tamamlandı. Sonuçlar işleniyor...\n");
                
                // Log dosyasını işle ve service_create.cmd'yi güncelle
                await ProcessBlockcheckResults();
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kurulum süreci başarıyla tamamlandı.\n");
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {ex.Message}\n");
                throw new Exception($"Zapret kurulum süreci başarısız: {ex.Message}", ex);
            }
        }

        private async Task UpdateScanLevel(string blockcheckShPath)
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                var scanLevel = GetSelectedScanLevel();
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCANLEVEL güncelleniyor: {scanLevel}\n");
                
                var content = await File.ReadAllTextAsync(blockcheckShPath);
                
                content = content.Replace("SCANLEVEL=${SCANLEVEL:-\"quick\"}", $"SCANLEVEL=${{SCANLEVEL:-\"{scanLevel}\"}}");
                content = content.Replace("SCANLEVEL=${SCANLEVEL:-\"standard\"}", $"SCANLEVEL=${{SCANLEVEL:-\"{scanLevel}\"}}");
                content = content.Replace("SCANLEVEL=${SCANLEVEL:-\"force\"}", $"SCANLEVEL=${{SCANLEVEL:-\"{scanLevel}\"}}");
                
                await File.WriteAllTextAsync(blockcheckShPath, content);
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCANLEVEL başarıyla güncellendi: {scanLevel}\n");
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCANLEVEL güncelleme hatası: {ex.Message}\n");
                throw new Exception($"SCANLEVEL güncelleme hatası: {ex.Message}", ex);
            }
        }

        private string GetSelectedScanLevel()
        {
            return cmbScanLevel.SelectedIndex switch
            {
                0 => "quick",
                1 => "standard",
                2 => "force",
                _ => "quick"
            };
        }

        private async Task ProcessBlockcheckResults()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck sonuçları işleniyor...\n");
                
                var localZapretPath = GetLocalAppDataZapretPath();
                var logPath = Path.Combine(localZapretPath, "blockcheck", "blockcheck.log");
                var serviceCreatePath = Path.Combine(localZapretPath, "zapret-winws", "service_create.cmd");
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log dosyası yolu: {logPath}\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Service create yolu: {serviceCreatePath}\n");
                
                if (!File.Exists(logPath))
                {
                    var errorMsg = "blockcheck.log dosyası bulunamadı.";
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {errorMsg}\n");
                    throw new Exception(errorMsg);
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log dosyası okunuyor...\n");
                var logContent = await File.ReadAllTextAsync(logPath);
                var summaryIndex = logContent.IndexOf("* SUMMARY");
                
                if (summaryIndex == -1)
                {
                    var errorMsg = "Log dosyasında SUMMARY bölümü bulunamadı.";
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {errorMsg}\n");
                    throw new Exception(errorMsg);
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SUMMARY bölümü bulundu. Parametreler aranıyor...\n");
                var lines = logContent.Substring(summaryIndex).Split('\n');
                string parameters = null;
                
                foreach (var line in lines.Skip(1))
                {
                    if (line.Contains("--wf-tcp=443"))
                    {
                        var tcpIndex = line.IndexOf("--wf-tcp=443");
                        if (tcpIndex >= 0)
                        {
                            parameters = line.Substring(tcpIndex + "--wf-tcp=443".Length).Trim();
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Parametreler bulundu: {parameters}\n");
                                        break;
                                    }
                                }
                            }

                if (string.IsNullOrEmpty(parameters))
                {
                    var errorMsg = "Log dosyasında gerekli parametreler bulunamadı.";
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {errorMsg}\n");
                    throw new Exception(errorMsg);
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Service create.cmd güncelleniyor...\n");
                
                // Yeni hizmet kurulum yöntemi: service_install_splitwireturkey.cmd
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Yeni hizmet kurulum yöntemi kullanılıyor...\n");
                
                // Parametreleri birleştir: temel parametreler + blockcheck'den gelen parametreler
                var fullParameters = $"--wf-tcp=80,443 --wf-udp=443,50000,50100 {parameters}";
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Birleştirilmiş parametreler: {fullParameters}\n");
                
                // Yeni hizmet kurulum dosyasını oluştur
                var serviceInstallPath = Path.Combine(localZapretPath, "zapret-winws", "service_install_splitwireturkey.cmd");
                await CreateServiceInstallScript(serviceInstallPath, fullParameters);
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti oluşturuldu: {serviceInstallPath}\n");
                
                // Hizmet kurulum scriptini çalıştır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti çalıştırılıyor...\n");
                
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{serviceInstallPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(serviceInstallPath),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum process başlatıldı. PID: {process.Id}\n");
                        
                        // Çıktıları oku
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        
                        process.WaitForExit();
                        
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum process kapandı. Exit Code: {process.ExitCode}\n");
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Standard Output: {output}\n");
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Standard Error: {error}\n");
                    }
                });

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti başarıyla çalıştırıldı.\n");
                
                // Hizmet kurulumunu doğrula
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulumu doğrulanıyor...\n");
                var serviceExists = await VerifyZapretService();
                
                if (serviceExists)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti başarıyla kuruldu ve çalışıyor.\n");
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck sonuçları başarıyla işlendi.\n");

                    System.Windows.MessageBox.Show("Zapret kurulumu başarıyla tamamlandı!\n\nHizmet adı: zapret\nDurum: Çalışıyor", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Başarılı kurulum sonrası kaldır butonunu güncelle
                    CheckZapretRemoveButtonVisibility();
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: Zapret hizmeti kurulamadı veya çalışmıyor.\n");
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blockcheck sonuçları işlendi ancak hizmet kurulumu başarısız.\n");

                    System.Windows.MessageBox.Show("Zapret kurulumu kısmen tamamlandı.\n\nBlockcheck tamamlandı ancak hizmet kurulumu başarısız oldu.\n\nLütfen manuel olarak kontrol edin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {ex.Message}\n");
                throw new Exception($"Blockcheck sonuçları işlenirken hata oluştu: {ex.Message}", ex);
            }
        }

        private async Task CreateServiceInstallScript(string scriptPath, string parameters)
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti oluşturuluyor...\n");
                
                var scriptContent = $@"@echo off
setlocal enabledelayedexpansion

rem Zapret hizmeti kurulum scripti
rem Bu script SplitWireTurkey tarafından otomatik olarak oluşturulur

set SERVICE_NAME=zapret
set EXE_PATH=%~dp0winws.exe
set DISPLAY_NAME=Zapret
set ARGS={parameters}

rem Mevcut hizmeti durdur ve sil
echo Mevcut %SERVICE_NAME% hizmeti kontrol ediliyor...
sc query %SERVICE_NAME% >nul 2>&1
if %errorlevel% equ 0 (
    echo %SERVICE_NAME% hizmeti bulundu, durduruluyor...
    net stop %SERVICE_NAME% >nul 2>&1
    echo %SERVICE_NAME% hizmeti siliniyor...
    sc delete %SERVICE_NAME% >nul 2>&1
    echo %SERVICE_NAME% hizmeti silindi.
) else (
    echo %SERVICE_NAME% hizmeti bulunamadı.
)

rem Yeni hizmeti oluştur
echo Yeni %SERVICE_NAME% hizmeti oluşturuluyor...
sc create %SERVICE_NAME% binPath= ""%EXE_PATH% %ARGS%"" DisplayName= ""%DISPLAY_NAME%"" start= auto

if %errorlevel% equ 0 (
    echo %SERVICE_NAME% hizmeti başarıyla oluşturuldu.
    
    rem Hizmet açıklamasını ayarla
    echo Hizmet açıklaması ayarlanıyor...
    sc description %SERVICE_NAME% ""zapret DPI bypass software""
    
    rem Hizmeti başlat
    echo %SERVICE_NAME% hizmeti başlatılıyor...
    sc start %SERVICE_NAME%
    
    if %errorlevel% equ 0 (
        echo %SERVICE_NAME% hizmeti başarıyla başlatıldı.
        echo Hizmet kurulumu tamamlandı.
    ) else (
        echo %SERVICE_NAME% hizmeti başlatılamadı. Hata kodu: %errorlevel%
    )
) else (
    echo %SERVICE_NAME% hizmeti oluşturulamadı. Hata kodu: %errorlevel%
)

echo.
echo Hizmet kurulum işlemi tamamlandı.
";
                
                await File.WriteAllTextAsync(scriptPath, scriptContent);
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti başarıyla oluşturuldu.\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Script içeriği:\n{scriptContent}\n");
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum scripti oluşturma hatası: {ex.Message}\n");
                throw new Exception($"Hizmet kurulum scripti oluşturma hatası: {ex.Message}", ex);
            }
        }

        private async Task<bool> VerifyZapretService()
        {
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti kontrol ediliyor...\n");
                
                // sc query komutu ile hizmet durumunu kontrol et
                var result = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = "query zapret",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        return new { Output = output, Error = error, ExitCode = process.ExitCode };
                    }
                    return new { Output = "", Error = "", ExitCode = -1 };
                });
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SC query sonucu - Exit Code: {result.ExitCode}\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SC query çıktısı: {result.Output}\n");
                if (!string.IsNullOrEmpty(result.Error))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SC query hatası: {result.Error}\n");
                }
                
                // Hizmet bulundu mu kontrol et
                if (result.ExitCode == 0 && result.Output.Contains("SERVICE_NAME: zapret"))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti bulundu.\n");
                    
                    // Hizmet durumunu kontrol et
                    if (result.Output.Contains("RUNNING") || result.Output.Contains("ÇALIŞIYOR"))
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti çalışıyor.\n");
                        return true;
                    }
                    else
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti bulundu ancak çalışmıyor.\n");
                        
                        // Hizmeti başlatmayı dene
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet başlatılmaya çalışılıyor...\n");
                        var startResult = await Task.Run(() =>
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "sc.exe",
                                Arguments = "start zapret",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                            
                            using var process = Process.Start(psi);
                            if (process != null)
                            {
                                var output = process.StandardOutput.ReadToEnd();
                                var error = process.StandardError.ReadToEnd();
                                process.WaitForExit();
                                
                                return new { Output = output, Error = error, ExitCode = process.ExitCode };
                            }
                            return new { Output = "", Error = "", ExitCode = -1 };
                        });
                        
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet başlatma sonucu - Exit Code: {startResult.ExitCode}\n");
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet başlatma çıktısı: {startResult.Output}\n");
                        
                        if (startResult.ExitCode == 0)
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti başarıyla başlatıldı.\n");
                            return true;
                        }
                        else
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti başlatılamadı.\n");
                            return false;
                        }
                    }
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti bulunamadı.\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet doğrulama hatası: {ex.Message}\n");
                return false;
            }
        }



        private async void BtnZapretCustomService_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Zapret Önayarlı Hizmet kurulumunu başlatmak istediğinizden emin misiniz?",
                "Zapret Önayarlı Hizmet Kurulumu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            loadingOverlay.Visibility = Visibility.Visible;
            
            try
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret özel hizmet kurulumu başlatılıyor...\n");
                
                // Önce hizmetleri durdur ve kaldır
                await PerformServiceCleanupForZapret();
                
                // Dosyalar yoksa kopyala
                if (!CheckZapretFilesExist())
                {
                    // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                    if (AreCriticalWinDivertFilesMissing())
                    {
                        System.Windows.MessageBox.Show("Kritik WinDivert dosyaları eksik olduğu için Zapret kurulumu yapılamıyor. Lütfen önce gerekli dosyaları ekleyin.", 
                            "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (!await EnsureZapretFilesExist())
                    {
                        return;
                    }
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret özel hizmet ön hazırlık işlemleri tamamlandı.\n");

                // Textbox'taki parametreleri al (blockcheck SUMMARY'den değil)
                var parameters = txtZapretParams.Text.Trim();
                
                if (string.IsNullOrEmpty(parameters))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Textbox'ta parametre bulunamadı!\n");
                    System.Windows.MessageBox.Show("Lütfen önce Zapret parametrelerini girin veya bir önayar seçin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kullanılacak parametreler: {parameters}\n");

                // DNS ayarları yap
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları yapılıyor...\n");
                await SetModernDNSSettingsAsync();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları tamamlandı.\n");

                // Zapret hizmetini kur (blockcheck olmadan, direkt parametrelerle)
                var success = await InstallZapretServiceDirectly(parameters, zapretLogPath);
                
                if (success)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret özel hizmet kurulumu başarıyla tamamlandı.\n");
                    System.Windows.MessageBox.Show("Zapret özel hizmet kurulumu başarıyla tamamlandı!", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Başarılı kurulum sonrası kaldır butonunu güncelle
                    CheckZapretRemoveButtonVisibility();
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret özel hizmet kurulumu başarısız!\n");
                    System.Windows.MessageBox.Show("Zapret özel hizmet kurulumu başarısız oldu. Lütfen log dosyasını kontrol edin.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret özel hizmet işlemi hatası: {ex.Message}\n");
                System.Windows.MessageBox.Show($"Zapret özel hizmet işlemi sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
            }
        }

        private async Task<string> RunCommandAsync(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        return $"Output: {output}, Error: {error}, ExitCode: {process.ExitCode}";
                    }
                    
                    return $"Output: {output}, ExitCode: {process.ExitCode}";
                }
                
                return "Process başlatılamadı";
            }
            catch (Exception ex)
            {
                return $"Hata: {ex.Message}";
            }
        }

        private async Task<bool> InstallZapretServiceDirectly(string parameters, string zapretLogPath)
        {
            try
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti direkt kurulumu başlatılıyor...\n");

                // Mevcut "zapret" hizmetini durdur ve kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Mevcut zapret hizmeti kontrol ediliyor...\n");
                
                try
                {
                    var stopResult = await RunCommandAsync("sc", "stop zapret");
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sc stop zapret sonucu: {stopResult}\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] zapret hizmeti durdurma hatası (muhtemelen hizmet yok): {ex.Message}\n");
                }

                try
                {
                    var deleteResult = await RunCommandAsync("sc", "delete zapret");
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sc delete zapret sonucu: {deleteResult}\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] zapret hizmeti silme hatası (muhtemelen hizmet yok): {ex.Message}\n");
                }

                // Hizmet silme işleminin tamamlanmasını bekle
                await Task.Delay(2000);

                // Yeni hizmet kurulum script'ini oluştur
                var localZapretPath = GetLocalAppDataZapretPath();
                var serviceInstallScriptPath = Path.Combine(localZapretPath, "zapret-winws", "service_install_splitwireturkey.cmd");

                // Parametreleri birleştir (--wf-tcp=80,443 --wf-udp=443,50000,50100 + kullanıcı parametreleri)
                var combinedParameters = $"--wf-tcp=80,443 --wf-udp=443,50000,50100 {parameters}";
                
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Birleştirilmiş parametreler: {combinedParameters}\n");

                // Hizmet kurulum script'ini oluştur
                await CreateServiceInstallScript(serviceInstallScriptPath, combinedParameters);
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum script'i oluşturuldu: {serviceInstallScriptPath}\n");

                // Script'i çalıştır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum script'i çalıştırılıyor...\n");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{serviceInstallScriptPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(serviceInstallScriptPath)
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum script'i tamamlandı. Exit Code: {process.ExitCode}\n");
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Çıktı: {output}\n");
                    if (!string.IsNullOrEmpty(error))
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hata: {error}\n");
                    }

                    if (process.ExitCode == 0)
                    {
                        // Hizmeti başlat
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti başlatılıyor...\n");
                        
                        var startResult = await RunCommandAsync("sc", "start zapret");
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sc start zapret sonucu: {startResult}\n");

                        // Hizmetin çalışıp çalışmadığını kontrol et
                        await Task.Delay(3000);
                        var verifyResult = await VerifyZapretService();
                        
                        if (verifyResult)
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti başarıyla kuruldu ve çalışıyor.\n");
                            return true;
                        }
                        else
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmeti kuruldu ancak çalışmıyor.\n");
                            return false;
                        }
                    }
                    else
                    {
                        File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum script'i başarısız oldu.\n");
                        return false;
                    }
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulum script'i başlatılamadı.\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] InstallZapretServiceDirectly hatası: {ex.Message}\n");
                return false;
            }
        }

        private async void BtnZapretCustomBatch_Click(object sender, RoutedEventArgs e)
        {
            // Dosyalar yoksa kopyala
            if (!CheckZapretFilesExist())
            {
                // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                if (AreCriticalWinDivertFilesMissing())
                {
                    System.Windows.MessageBox.Show("Kritik WinDivert dosyaları eksik olduğu için Zapret işlemi yapılamıyor. Lütfen önce gerekli dosyaları ekleyin.", 
                        "İşlem Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (!await EnsureZapretFilesExist())
                {
                    return;
                }
            }

            // Loading ekranını göster
            loadingOverlay.Visibility = Visibility.Visible;

            try
            {
                // Textbox'taki parametreleri al
                var parameters = txtZapretParams.Text.Trim();
                
                if (string.IsNullOrEmpty(parameters))
                {
                    System.Windows.MessageBox.Show("Lütfen önce Zapret parametrelerini girin.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret Özel Tek Seferlik başlatılıyor...\n");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Parametreler: {parameters}\n");

                // DNS ayarları yap
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları yapılıyor...\n");
                await SetModernDNSSettingsAsync();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları tamamlandı.\n");

                // onetime_batch.cmd dosyasının yolunu al
                var localZapretPath = GetLocalAppDataZapretPath();
                var onetimeBatchPath = Path.Combine(localZapretPath, "zapret-winws", "onetime_batch.cmd");

                if (!File.Exists(onetimeBatchPath))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: onetime_batch.cmd dosyası bulunamadı: {onetimeBatchPath}\n");
                    System.Windows.MessageBox.Show("onetime_batch.cmd dosyası bulunamadı. Lütfen programı yeniden yükleyin.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] onetime_batch.cmd çalıştırılıyor: {onetimeBatchPath}\n");

                // CMD dosyasını parametrelerle çalıştır
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{onetimeBatchPath}\" {parameters}\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(onetimeBatchPath)
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] onetime_batch.cmd başlatıldı. Process ID: {process.Id}\n");
                    loadingOverlay.Visibility = Visibility.Collapsed;
                    // Process'in tamamlanmasını bekle
                    await Task.Run(() => process.WaitForExit());
                    
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] onetime_batch.cmd tamamlandı. Exit Code: {process.ExitCode}\n");
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: onetime_batch.cmd başlatılamadı!\n");
                    System.Windows.MessageBox.Show("onetime_batch.cmd başlatılamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                var zapretLogPath = GetZapretLogPath();
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret Özel Tek Seferlik hatası: {ex.Message}\n");
                
                System.Windows.MessageBox.Show($"Zapret Özel Tek Seferlik sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Loading ekranını gizle
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnRemoveZapret_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Zapret'i kaldırmak istediğinizden emin misiniz? Bu işlem Zapret hizmetlerini durduracak ve kaldıracaktır.",
                "Zapret Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                loadingOverlay.Visibility = Visibility.Visible;
                
                try
                {
                    var zapretLogPath = GetZapretLogPath();
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kaldırma işlemi başlatılıyor...\n");

                    await RemoveZapretServices(zapretLogPath);

                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kaldırma işlemi tamamlandı.\n");
                    
                    System.Windows.MessageBox.Show("Zapret başarıyla kaldırıldı!", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    var zapretLogPath = GetZapretLogPath();
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret kaldırma hatası: {ex.Message}\n");
                    
                    System.Windows.MessageBox.Show($"Zapret kaldırma sırasında hata oluştu: {ex.Message}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    loadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task RemoveZapretServices(string zapretLogPath)
        {
            try
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmetleri kaldırılıyor...\n");

                // 1. Zapret hizmetini durdur ve kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 1. Zapret hizmeti durduruluyor...\n");
                await StopAndRemoveService("zapret", zapretLogPath);

                // 2. winws1 hizmetini durdur ve kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 2. winws1 hizmeti durduruluyor...\n");
                await StopAndRemoveService("winws1", zapretLogPath);

                // 3. winws2 hizmetini durdur ve kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 3. winws2 hizmeti durduruluyor...\n");
                await StopAndRemoveService("winws2", zapretLogPath);

                // 4. winws.exe işlemlerini durdur
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 4. winws.exe işlemleri durduruluyor...\n");
                await StopWinwsProcesses(zapretLogPath);

                // 5. WinDivert hizmetini durdur ve kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 5. WinDivert hizmeti durduruluyor...\n");
                await StopAndRemoveService("WinDivert", zapretLogPath);

                // Hizmet kaldırma işlemlerinin tamamlanmasını bekle
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kaldırma işlemlerinin tamamlanması bekleniyor...\n");
                await Task.Delay(3000);

                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Zapret hizmetleri başarıyla kaldırıldı.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RemoveZapretServices hatası: {ex.Message}\n");
                throw;
            }
        }

        private async Task StopAndRemoveService(string serviceName, string zapretLogPath)
        {
            try
            {
                // Hizmeti durdur
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {serviceName} hizmeti durduruluyor...\n");
                var stopResult = await RunCommandAsync("sc", $"stop {serviceName}");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sc stop {serviceName} sonucu: {stopResult}\n");

                // Hizmetin durmasını bekle
                await Task.Delay(2000);

                // Hizmeti kaldır
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {serviceName} hizmeti kaldırılıyor...\n");
                var deleteResult = await RunCommandAsync("sc", $"delete {serviceName}");
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sc delete {serviceName} sonucu: {deleteResult}\n");

                // Hizmetin kaldırılmasını bekle
                await Task.Delay(1000);

                // Hizmetin gerçekten kaldırılıp kaldırılmadığını kontrol et
                var queryResult = await RunCommandAsync("sc", $"query {serviceName}");
                if (queryResult.Contains("The specified service does not exist") || queryResult.Contains("belirtilen hizmet mevcut değil"))
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {serviceName} hizmeti başarıyla kaldırıldı.\n");
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {serviceName} hizmeti kaldırılamadı veya hala mevcut.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {serviceName} hizmeti kaldırırken hata: {ex.Message}\n");
                // Hata olsa bile devam et
            }
        }

        private async Task StopWinwsProcesses(string zapretLogPath)
        {
            try
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] winws.exe işlemleri aranıyor...\n");

                var processes = Process.GetProcessesByName("winws");
                if (processes.Length > 0)
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {processes.Length} adet winws.exe işlemi bulundu.\n");

                    foreach (var process in processes)
                    {
                        try
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] winws.exe işlemi durduruluyor (PID: {process.Id})...\n");
                            process.Kill();
                            await process.WaitForExitAsync();
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] winws.exe işlemi başarıyla durduruldu (PID: {process.Id}).\n");
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] winws.exe işlemi durdurulurken hata (PID: {process.Id}): {ex.Message}\n");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                else
                {
                    File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hiç winws.exe işlemi bulunamadı.\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(zapretLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] StopWinwsProcesses hatası: {ex.Message}\n");
                // Hata olsa bile devam et
            }
        }

        #endregion

        // Service Remove Button Click Handlers
        private async void BtnWireSockRemove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("wiresock-client-service");
        }



        private async void BtnByeDPIRemove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("ByeDPI");
        }

        private async void BtnProxiFyreRemove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("ProxiFyreService");
        }

        private async void BtnWinWS1Remove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("winws1");
        }

        private async void BtnWinWS2Remove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("winws2");
        }

        private async void BtnZapretServiceRemove_Click(object sender, RoutedEventArgs e)
        {
            await RemoveService("zapret");
        }

        private async void BtnWinDivertRemove_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "WinDivert'ı kaldırmak istediğinizden emin misiniz? Bu işlem WinDivert hizmetini durduracak ve kaldıracaktır.",
                "WinDivert Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                // Önce GoodbyeDPI ve Zapret hizmetlerini kontrol et ve varsa kaldır
                if (IsServiceInstalled("GoodbyeDPI"))
                {
                    System.Windows.MessageBox.Show("WinDivert kaldırılmadan önce GoodbyeDPI hizmeti kaldırılıyor...", 
                        "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RemoveService("GoodbyeDPI");
                }
                
                if (IsServiceInstalled("zapret"))
                {
                    System.Windows.MessageBox.Show("WinDivert kaldırılmadan önce Zapret hizmeti kaldırılıyor...", 
                        "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RemoveService("zapret");
                }
                
                // Şimdi WinDivert'ı kaldır
            await RemoveService("WinDivert");
            }
            finally
            {
                ShowLoading(false);
                CheckAllServices();
            }
        }
        
        private bool IsServiceInstalled(string serviceName)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"query {serviceName}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains("SERVICE_NAME:") && !output.Contains("1060");
            }
            catch
            {
                return false;
            }
        }
        
        private void CheckByeDPIRemoveButtonVisibility()
        {
            // ByeDPI hizmeti yüklü değilse kaldır butonunu gizle
            if (btnRemoveByeDPI != null)
            {
                btnRemoveByeDPI.Visibility = IsServiceInstalled("ByeDPI") ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        private void CheckZapretRemoveButtonVisibility()
        {
            // Zapret hizmeti yüklü değilse kaldır butonunu gizle
            if (btnRemoveZapret != null)
            {
                btnRemoveZapret.Visibility = IsServiceInstalled("zapret") ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        private void CheckGoodbyeDPIRemoveButtonVisibility()
        {
            // GoodbyeDPI hizmeti yüklü değilse kaldır butonunu gizle
            if (btnRemoveGoodbyeDPI != null)
            {
                btnRemoveGoodbyeDPI.Visibility = IsServiceInstalled("GoodbyeDPI") ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Cache'den hızlı kontrol metodları
        private void CheckByeDPIRemoveButtonVisibilityFromCache()
        {
            if (btnRemoveByeDPI != null)
            {
                if (_serviceStatusCache.TryGetValue("ByeDPI", out bool isInstalled))
                {
                    btnRemoveByeDPI.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    // Cache'de yoksa asenkron kontrol yap
                    _ = Task.Run(async () => await CheckAllServicesAsync());
                }
            }
        }
        
        private void CheckZapretRemoveButtonVisibilityFromCache()
        {
            if (btnRemoveZapret != null)
            {
                if (_serviceStatusCache.TryGetValue("zapret", out bool isInstalled))
                {
                    btnRemoveZapret.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    // Cache'de yoksa asenkron kontrol yap
                    _ = Task.Run(async () => await CheckAllServicesAsync());
                }
            }
        }
        
        private void CheckGoodbyeDPIRemoveButtonVisibilityFromCache()
        {
            if (btnRemoveGoodbyeDPI != null)
            {
                if (_serviceStatusCache.TryGetValue("GoodbyeDPI", out bool isInstalled))
                {
                    btnRemoveGoodbyeDPI.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    // Cache'de yoksa asenkron kontrol yap
                    _ = Task.Run(async () => await CheckAllServicesAsync());
                }
            }
        }

        private async void BtnDroverRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show("Drover dosyalarını kaldırmak istediğinizden emin misiniz? Discord kapatılacak ve drover dosyaları silinecektir.", 
                    "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes) return;

                ShowLoading(true);

                // Discord.exe'yi durdur
                var discordStopSuccess = await StopDiscordProcessAsync();
                if (!discordStopSuccess)
                {
                    System.Windows.MessageBox.Show("Discord kapatılamadı. Drover dosyaları kaldırılamayacak.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowLoading(false);
                    return;
                }

                // Kısa bir bekleme süresi ekle
                await Task.Delay(2000);

                var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                bool filesRemoved = false;

                // Discord app-* klasörlerinde drover dosyalarını ara ve sil
                if (Directory.Exists(discordPath))
                {
                    var appFolders = Directory.GetDirectories(discordPath, "app-*");
                    foreach (var appFolder in appFolders)
                    {
                        var versionDllPath = Path.Combine(appFolder, "version.dll");
                        var droverIniPath = Path.Combine(appFolder, "drover.ini");

                        if (File.Exists(versionDllPath))
                        {
                            try
                            {
                                File.Delete(versionDllPath);
                                filesRemoved = true;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"version.dll silinirken hata: {ex.Message}");
                            }
                        }

                        if (File.Exists(droverIniPath))
                        {
                            try
                            {
                                File.Delete(droverIniPath);
                                filesRemoved = true;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"drover.ini silinirken hata: {ex.Message}");
                            }
                        }
                    }
                }

                ShowLoading(false);

                if (filesRemoved)
                {
                    // UI'yi güncelle
                    droverStatus.Fill = System.Windows.Media.Brushes.Red;
                    droverStatusText.Text = "Yüklü değil";
                    btnDroverRemove.Visibility = Visibility.Collapsed;

                    System.Windows.MessageBox.Show("Drover dosyaları başarıyla kaldırıldı.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Drover durumunu güncelle
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await CheckDroverFilesAsync();
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show("Drover dosyaları bulunamadı veya silinemedi.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Windows.MessageBox.Show($"Drover dosyaları kaldırılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RemoveService(string serviceName)
        {
            try
            {
                var result = System.Windows.MessageBox.Show($"{serviceName} hizmetini kaldırmak istediğinizden emin misiniz?", 
                    "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes) return;

                ShowLoading(true);

                // Hizmeti durdur
                ExecuteCommand("sc", $"stop {serviceName}");
                await Task.Delay(2000);

                // Hizmeti kaldır
                ExecuteCommand("sc", $"delete {serviceName}");
                await Task.Delay(1000);

                ShowLoading(false);
                System.Windows.MessageBox.Show($"{serviceName} hizmeti başarıyla kaldırıldı.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Hizmet durumlarını güncelle - UI thread'de çalıştır
                await Dispatcher.InvokeAsync(() =>
                {
                    // Kaldırılan hizmetin durumunu hemen güncelle
                    UpdateRemovedServiceStatus(serviceName);
                });
                
                // Tüm hizmet durumlarını kapsamlı şekilde yenile
                await ForceRefreshAllServicesAsync();
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Windows.MessageBox.Show($"{serviceName} hizmeti kaldırılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Hata durumunda da hizmet durumlarını güncelle
                await ForceRefreshAllServicesAsync();
            }
        }

        private System.Windows.Shapes.Ellipse GetStatusEllipse(string serviceName)
        {
            return serviceName switch
            {
                "wiresock-client-service" => wireSockStatus,
                "ByeDPI" => byeDPIStatus,
                "ProxiFyreService" => proxiFyreStatus,
                "winws1" => winWS1Status,
                "winws2" => winWS2Status,
                "zapret" => zapretStatus,
                "WinDivert" => winDivertStatus,
                "GoodbyeDPI" => null,
                _ => null
            };
        }

        private TextBlock GetStatusTextBlock(string serviceName)
        {
            return serviceName switch
            {
                "wiresock-client-service" => wireSockStatusText,
                "ByeDPI" => byeDPIStatusText,
                "ProxiFyreService" => proxiFyreStatusText,
                "winws1" => winWS1StatusText,
                "winws2" => winWS2StatusText,
                "zapret" => zapretStatusText,
                "WinDivert" => winDivertStatusText,
                "GoodbyeDPI" => null,
                _ => null
            };
        }

        private System.Windows.Controls.Button GetRemoveButton(string serviceName)
        {
            return serviceName switch
            {
                "wiresock-client-service" => btnWireSockRemove,
                "ByeDPI" => btnByeDPIRemove, // Gelişmiş sayfa butonu
                "ProxiFyreService" => btnProxiFyreRemove,
                "winws1" => btnWinWS1Remove,
                "winws2" => btnWinWS2Remove,
                "zapret" => btnZapretServiceRemove,
                "WinDivert" => btnWinDivertRemove,
                "GoodbyeDPI" => btnRemoveGoodbyeDPI,
                _ => null
            };
        }

        #region GoodbyeDPI Methods

        private void CmbGoodbyeDPIPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbGoodbyeDPIPresets.SelectedItem is ComboBoxItem selectedItem)
            {
                var presetName = selectedItem.Content.ToString();
                var parameters = GetGoodbyeDPIPresetParameters(presetName);
                txtGoodbyeDPIParams.Text = parameters;
            }
        }

        private void ChkGoodbyeDPIManualParams_Checked(object sender, RoutedEventArgs e)
        {
            txtGoodbyeDPIParams.Visibility = Visibility.Visible;
            _goodbyeDPIManualParamsActive = true;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }

        private void ChkGoodbyeDPIManualParams_Unchecked(object sender, RoutedEventArgs e)
        {
            txtGoodbyeDPIParams.Visibility = Visibility.Collapsed;
            _goodbyeDPIManualParamsActive = false;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }

        private void ChkGoodbyeDPIUseBlacklist_Checked(object sender, RoutedEventArgs e)
        {
            editBlacklistPanel.Visibility = Visibility.Visible;
            _goodbyeDPIUseBlacklistActive = true;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }

        private void ChkGoodbyeDPIUseBlacklist_Unchecked(object sender, RoutedEventArgs e)
        {
            editBlacklistPanel.Visibility = Visibility.Collapsed;
            chkGoodbyeDPIEditBlacklist.IsChecked = false;
            txtGoodbyeDPIBlacklist.Visibility = Visibility.Collapsed;
            btnGoodbyeDPISaveBlacklist.Visibility = Visibility.Collapsed;
            _goodbyeDPIUseBlacklistActive = false;
            _goodbyeDPIEditBlacklistActive = false;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }

        private void ChkGoodbyeDPIEditBlacklist_Checked(object sender, RoutedEventArgs e)
        {
            txtGoodbyeDPIBlacklist.Visibility = Visibility.Visible;
            btnGoodbyeDPISaveBlacklist.Visibility = Visibility.Visible;
            LoadGoodbyeDPIBlacklist();
            _goodbyeDPIEditBlacklistActive = true;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }

        private void ChkGoodbyeDPIEditBlacklist_Unchecked(object sender, RoutedEventArgs e)
        {
            txtGoodbyeDPIBlacklist.Visibility = Visibility.Collapsed;
            btnGoodbyeDPISaveBlacklist.Visibility = Visibility.Collapsed;
            _goodbyeDPIEditBlacklistActive = false;
            
            // Merkezi boyut hesaplama ve güncelleme
            UpdateGoodbyeDPIWindowSize();
        }
        
        // Ana Sayfa pencere boyutunu merkezi olarak güncelleyen metod
        private void UpdateMainPageWindowSize()
        {
            var animationDuration = TimeSpan.FromMilliseconds(400);
            var totalHeight = _mainPageAdvancedSettingsActive ? _mainPageAdvancedSettingsHeight : _mainPageBaseHeight;
            
            // Pencere boyutunu animasyonlu olarak güncelle
            AnimateWindowHeight(totalHeight, animationDuration);
        }
        
        // Zapret pencere boyutunu merkezi olarak güncelleyen metod
        private void UpdateZapretWindowSize()
        {
            var animationDuration = TimeSpan.FromMilliseconds(400);
            var totalHeight = _zapretManualParamsActive ? _zapretManualParamsHeight : _zapretBaseHeight;
            
            // Pencere boyutunu animasyonlu olarak güncelle
            AnimateWindowHeight(totalHeight, animationDuration);
        }
        
        // GoodbyeDPI pencere boyutunu merkezi olarak güncelleyen metod
        private void UpdateGoodbyeDPIWindowSize()
        {
            var animationDuration = TimeSpan.FromMilliseconds(400);
            var totalHeight = _goodbyeDPIBaseHeight;
            
            // Aktif switch'lere göre boyut hesapla
            if (_goodbyeDPIManualParamsActive)
                totalHeight += _goodbyeDPIManualParamsHeight;
            
            if (_goodbyeDPIUseBlacklistActive)
                totalHeight += _goodbyeDPIUseBlacklistHeight;
            
            if (_goodbyeDPIEditBlacklistActive)
                totalHeight += _goodbyeDPIEditBlacklistHeight;
            
            // Pencere boyutunu animasyonlu olarak güncelle
            AnimateWindowHeight(totalHeight, animationDuration);
        }

        // Görev çubuğu karanlık mod desteğini kontrol eden metod
        private void CheckTaskbarDarkModeSupport()
        {
            try
            {
                // Windows 10 Build 15063 (Creators Update) ve üzeri kontrol
                var osVersion = Environment.OSVersion;
                var majorVersion = osVersion.Version.Major;
                var minorVersion = osVersion.Version.Minor;
                var buildNumber = osVersion.Version.Build;

                // Windows 10 ve üzeri, Build 15063 ve üzeri
                _isTaskbarDarkModeSupported = (majorVersion == 10 && buildNumber >= 15063) || majorVersion > 10;
                
                Debug.WriteLine($"Görev çubuğu karanlık mod desteği: {_isTaskbarDarkModeSupported} (Windows {majorVersion}.{minorVersion}.{buildNumber})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Görev çubuğu karanlık mod desteği kontrol edilirken hata: {ex.Message}");
                _isTaskbarDarkModeSupported = false;
            }
        }

        // Görev çubuğunu karanlık moda alan metod
        private void SetTaskbarDarkMode(bool isDarkMode)
        {
            if (!_isTaskbarDarkModeSupported)
            {
                Debug.WriteLine("Görev çubuğu karanlık mod bu Windows sürümünde desteklenmiyor");
                return;
            }

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine("Pencere handle alınamadı");
                    return;
                }

                int value = isDarkMode ? 1 : 0;
                int result;

                // Windows 10 20H1 ve üzeri için
                result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                if (result != 0)
                {
                    // Eski Windows 10 sürümleri için
                    result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
                }

                if (result == 0)
                {
                    Debug.WriteLine($"Görev çubuğu karanlık mod {(isDarkMode ? "açıldı" : "kapatıldı")}");
                }
                else
                {
                    Debug.WriteLine($"Görev çubuğu karanlık mod ayarlanamadı. Hata kodu: {result}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Görev çubuğu karanlık mod ayarlanırken hata: {ex.Message}");
            }
        }

        // Kaspersky ve WARP uyumluluk kontrolü
        private async Task CheckCompatibilityAsync()
        {
            try
            {
                await Dispatcher.InvokeAsync(() => {
                    loadingOverlay.Visibility = Visibility.Visible;
                });

                // Kaspersky antivirüs kontrolü - dosya ve process
                await CheckKasperskyAntivirus();
                
                // Kaspersky VPN kontrolü - process
                await CheckKasperskyVpn();
                
                // Cloudflare WARP kontrolü - process
                await CheckCloudflareWarp();

                // UI güncellemelerini main thread'de yap
                await Dispatcher.InvokeAsync(() => {
                    // Program başlangıcında overlay'leri gösterme, sadece durumları güncelle
                    // Overlay'ler sadece Zapret/GoodbyeDPI sekmelerine geçildiğinde görünecek
                    loadingOverlay.Visibility = Visibility.Collapsed;
                });

                Debug.WriteLine($"Uyumluluk kontrolü tamamlandı - Kaspersky: {_isKasperskyDetected}, KasperskyVPN: {_isKasperskyVpnDetected}, WARP: {_isCloudflareWarpDetected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Uyumluluk kontrolü sırasında hata: {ex.Message}");
                await Dispatcher.InvokeAsync(() => {
                    loadingOverlay.Visibility = Visibility.Collapsed;
                });
            }
        }

        // Kaspersky antivirüs kontrolü
        private async Task CheckKasperskyAntivirus()
        {
            await Task.Run(() => {
                try
                {
                    // Kritik WinDivert dosyalarını kontrol et
                    var result = AreCriticalWinDivertFilesMissing();
                    
                    // Kaspersky tespit edildi mi: kritik WinDivert dosyaları eksikse
                    _isKasperskyDetected = result;

                    Debug.WriteLine($"Kaspersky kontrolü - Kritik dosyalar eksik: {result}, Tespit edildi: {_isKasperskyDetected}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Kaspersky antivirüs kontrolü sırasında hata: {ex.Message}");
                    _isKasperskyDetected = false;
                }
            });
        }

        // Kaspersky VPN kontrolü
        private async Task CheckKasperskyVpn()
        {
            await Task.Run(() => {
                try
                {
                    var kasperskyVpnProcesses = new[] { "ksde.exe", "ksdeui.exe" };
                    _isKasperskyVpnDetected = kasperskyVpnProcesses.Any(processName => 
                        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).Length > 0);

                    Debug.WriteLine($"Kaspersky VPN kontrolü - Tespit edildi: {_isKasperskyVpnDetected}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Kaspersky VPN kontrolü sırasında hata: {ex.Message}");
                    _isKasperskyVpnDetected = false;
                }
            });
        }

        // Cloudflare WARP kontrolü
        private async Task CheckCloudflareWarp()
        {
            await Task.Run(() => {
                try
                {
                    var warpProcesses = new[] { "warp-svc.exe", "warp-taskbar.exe", "warp-cli.exe" };
                    _isCloudflareWarpDetected = warpProcesses.Any(processName => 
                        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).Length > 0);

                    Debug.WriteLine($"Cloudflare WARP kontrolü - Tespit edildi: {_isCloudflareWarpDetected}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Cloudflare WARP kontrolü sırasında hata: {ex.Message}");
                    _isCloudflareWarpDetected = false;
                }
            });
        }

        // Overlay görünürlüğünü güncelle
        private void UpdateOverlayVisibility()
        {
            try
            {
                // Öncelikle tüm overlay'leri gizle
                kasperskyOverlay.Visibility = Visibility.Collapsed;
                kasperskyVpnOverlay.Visibility = Visibility.Collapsed;
                cloudflareWarpOverlay.Visibility = Visibility.Collapsed;

                // Öncelik sırası: Kaspersky Antivirüs > Kaspersky VPN > Cloudflare WARP
                if (_isKasperskyDetected)
                {
                    kasperskyOverlay.Visibility = Visibility.Visible;
                    Debug.WriteLine("Kaspersky antivirüs overlay gösteriliyor");
                }
                else if (_isKasperskyVpnDetected)
                {
                    kasperskyVpnOverlay.Visibility = Visibility.Visible;
                    Debug.WriteLine("Kaspersky VPN overlay gösteriliyor");
                }
                else if (_isCloudflareWarpDetected)
                {
                    cloudflareWarpOverlay.Visibility = Visibility.Visible;
                    Debug.WriteLine("Cloudflare WARP overlay gösteriliyor");
                }

                // Tema renklerini güncelle
                UpdateOverlayTheme();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Overlay görünürlük güncellemesi sırasında hata: {ex.Message}");
            }
        }

        // Overlay tema renklerini güncelle
        private void UpdateOverlayTheme()
        {
            try
            {
                bool isDarkMode = btnThemeToggle?.IsChecked == true;
                
                var backgroundColor = isDarkMode ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80404040")) 
                                                 : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80808080"));
                
                var textColor = isDarkMode ? Brushes.White : Brushes.Black;
                
                var stripeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196f3"));

                // Tüm overlay'ler için tema uygula
                kasperskyOverlay.Background = backgroundColor;
                kasperskyVpnOverlay.Background = backgroundColor;
                cloudflareWarpOverlay.Background = backgroundColor;

                kasperskyText.Foreground = textColor;
                kasperskyVpnText.Foreground = textColor;
                cloudflareWarpText.Foreground = textColor;

                // Gölge efektlerini tema moduna göre ayarla
                UpdateOverlayShadows(isDarkMode);

                // Şerit renklerini güncelle
                UpdateStripeColors(stripeColor);

                Debug.WriteLine($"Overlay tema güncellendi - Karanlık mod: {isDarkMode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Overlay tema güncellemesi sırasında hata: {ex.Message}");
            }
        }

        // Şerit renklerini güncelle
        private void UpdateStripeColors(SolidColorBrush stripeColor)
        {
            try
            {
                // Kaspersky Antivirüs şeritleri
                if (kasperskyStripe1 != null) kasperskyStripe1.Fill = stripeColor;
                if (kasperskyStripe2 != null) kasperskyStripe2.Fill = stripeColor;
                if (kasperskyStripe3 != null) kasperskyStripe3.Fill = stripeColor;
                if (kasperskyStripe4 != null) kasperskyStripe4.Fill = stripeColor;
                if (kasperskyStripe5 != null) kasperskyStripe5.Fill = stripeColor;
                if (kasperskyStripe6 != null) kasperskyStripe6.Fill = stripeColor;
                if (kasperskyStripe7 != null) kasperskyStripe7.Fill = stripeColor;
                if (kasperskyStripe8 != null) kasperskyStripe8.Fill = stripeColor;
                if (kasperskyStripe9 != null) kasperskyStripe9.Fill = stripeColor;
                if (kasperskyStripe10 != null) kasperskyStripe10.Fill = stripeColor;
                if (kasperskyStripe11 != null) kasperskyStripe11.Fill = stripeColor;
                if (kasperskyStripe12 != null) kasperskyStripe12.Fill = stripeColor;
                if (kasperskyStripe13 != null) kasperskyStripe13.Fill = stripeColor;
                if (kasperskyStripe14 != null) kasperskyStripe14.Fill = stripeColor;
                if (kasperskyStripe15 != null) kasperskyStripe15.Fill = stripeColor;
                if (kasperskyStripe16 != null) kasperskyStripe16.Fill = stripeColor;
                if (kasperskyStripe17 != null) kasperskyStripe17.Fill = stripeColor;
                if (kasperskyStripe18 != null) kasperskyStripe18.Fill = stripeColor;
                if (kasperskyStripe19 != null) kasperskyStripe19.Fill = stripeColor;
                if (kasperskyStripe20 != null) kasperskyStripe20.Fill = stripeColor;
                if (kasperskyStripe21 != null) kasperskyStripe21.Fill = stripeColor;
                if (kasperskyStripe22 != null) kasperskyStripe22.Fill = stripeColor;
                if (kasperskyStripe23 != null) kasperskyStripe23.Fill = stripeColor;
                if (kasperskyStripe24 != null) kasperskyStripe24.Fill = stripeColor;
                if (kasperskyStripe25 != null) kasperskyStripe25.Fill = stripeColor;
                if (kasperskyStripe26 != null) kasperskyStripe26.Fill = stripeColor;
                if (kasperskyStripe27 != null) kasperskyStripe27.Fill = stripeColor;
                if (kasperskyStripe28 != null) kasperskyStripe28.Fill = stripeColor;
                if (kasperskyStripe29 != null) kasperskyStripe29.Fill = stripeColor;
                if (kasperskyStripe30 != null) kasperskyStripe30.Fill = stripeColor;

                // Kaspersky VPN şeritleri
                if (kasperskyVpnStripe1 != null) kasperskyVpnStripe1.Fill = stripeColor;
                if (kasperskyVpnStripe2 != null) kasperskyVpnStripe2.Fill = stripeColor;
                if (kasperskyVpnStripe3 != null) kasperskyVpnStripe3.Fill = stripeColor;
                if (kasperskyVpnStripe4 != null) kasperskyVpnStripe4.Fill = stripeColor;
                if (kasperskyVpnStripe5 != null) kasperskyVpnStripe5.Fill = stripeColor;
                if (kasperskyVpnStripe6 != null) kasperskyVpnStripe6.Fill = stripeColor;
                if (kasperskyVpnStripe7 != null) kasperskyVpnStripe7.Fill = stripeColor;
                if (kasperskyVpnStripe8 != null) kasperskyVpnStripe8.Fill = stripeColor;
                if (kasperskyVpnStripe9 != null) kasperskyVpnStripe9.Fill = stripeColor;
                if (kasperskyVpnStripe10 != null) kasperskyVpnStripe10.Fill = stripeColor;
                if (kasperskyVpnStripe11 != null) kasperskyVpnStripe11.Fill = stripeColor;
                if (kasperskyVpnStripe12 != null) kasperskyVpnStripe12.Fill = stripeColor;
                if (kasperskyVpnStripe13 != null) kasperskyVpnStripe13.Fill = stripeColor;
                if (kasperskyVpnStripe14 != null) kasperskyVpnStripe14.Fill = stripeColor;
                if (kasperskyVpnStripe15 != null) kasperskyVpnStripe15.Fill = stripeColor;
                if (kasperskyVpnStripe16 != null) kasperskyVpnStripe16.Fill = stripeColor;
                if (kasperskyVpnStripe17 != null) kasperskyVpnStripe17.Fill = stripeColor;
                if (kasperskyVpnStripe18 != null) kasperskyVpnStripe18.Fill = stripeColor;
                if (kasperskyVpnStripe19 != null) kasperskyVpnStripe19.Fill = stripeColor;
                if (kasperskyVpnStripe20 != null) kasperskyVpnStripe20.Fill = stripeColor;
                if (kasperskyVpnStripe21 != null) kasperskyVpnStripe21.Fill = stripeColor;
                if (kasperskyVpnStripe22 != null) kasperskyVpnStripe22.Fill = stripeColor;
                if (kasperskyVpnStripe23 != null) kasperskyVpnStripe23.Fill = stripeColor;
                if (kasperskyVpnStripe24 != null) kasperskyVpnStripe24.Fill = stripeColor;
                if (kasperskyVpnStripe25 != null) kasperskyVpnStripe25.Fill = stripeColor;
                if (kasperskyVpnStripe26 != null) kasperskyVpnStripe26.Fill = stripeColor;
                if (kasperskyVpnStripe27 != null) kasperskyVpnStripe27.Fill = stripeColor;
                if (kasperskyVpnStripe28 != null) kasperskyVpnStripe28.Fill = stripeColor;
                if (kasperskyVpnStripe29 != null) kasperskyVpnStripe29.Fill = stripeColor;
                if (kasperskyVpnStripe30 != null) kasperskyVpnStripe30.Fill = stripeColor;

                // Cloudflare WARP şeritleri
                if (cloudflareWarpStripe1 != null) cloudflareWarpStripe1.Fill = stripeColor;
                if (cloudflareWarpStripe2 != null) cloudflareWarpStripe2.Fill = stripeColor;
                if (cloudflareWarpStripe3 != null) cloudflareWarpStripe3.Fill = stripeColor;
                if (cloudflareWarpStripe4 != null) cloudflareWarpStripe4.Fill = stripeColor;
                if (cloudflareWarpStripe5 != null) cloudflareWarpStripe5.Fill = stripeColor;
                if (cloudflareWarpStripe6 != null) cloudflareWarpStripe6.Fill = stripeColor;
                if (cloudflareWarpStripe7 != null) cloudflareWarpStripe7.Fill = stripeColor;
                if (cloudflareWarpStripe8 != null) cloudflareWarpStripe8.Fill = stripeColor;
                if (cloudflareWarpStripe9 != null) cloudflareWarpStripe9.Fill = stripeColor;
                if (cloudflareWarpStripe10 != null) cloudflareWarpStripe10.Fill = stripeColor;
                if (cloudflareWarpStripe11 != null) cloudflareWarpStripe11.Fill = stripeColor;
                if (cloudflareWarpStripe12 != null) cloudflareWarpStripe12.Fill = stripeColor;
                if (cloudflareWarpStripe13 != null) cloudflareWarpStripe13.Fill = stripeColor;
                if (cloudflareWarpStripe14 != null) cloudflareWarpStripe14.Fill = stripeColor;
                if (cloudflareWarpStripe15 != null) cloudflareWarpStripe15.Fill = stripeColor;
                if (cloudflareWarpStripe16 != null) cloudflareWarpStripe16.Fill = stripeColor;
                if (cloudflareWarpStripe17 != null) cloudflareWarpStripe17.Fill = stripeColor;
                if (cloudflareWarpStripe18 != null) cloudflareWarpStripe18.Fill = stripeColor;
                if (cloudflareWarpStripe19 != null) cloudflareWarpStripe19.Fill = stripeColor;
                if (cloudflareWarpStripe20 != null) cloudflareWarpStripe20.Fill = stripeColor;
                if (cloudflareWarpStripe21 != null) cloudflareWarpStripe21.Fill = stripeColor;
                if (cloudflareWarpStripe22 != null) cloudflareWarpStripe22.Fill = stripeColor;
                if (cloudflareWarpStripe23 != null) cloudflareWarpStripe23.Fill = stripeColor;
                if (cloudflareWarpStripe24 != null) cloudflareWarpStripe24.Fill = stripeColor;
                if (cloudflareWarpStripe25 != null) cloudflareWarpStripe25.Fill = stripeColor;
                if (cloudflareWarpStripe26 != null) cloudflareWarpStripe26.Fill = stripeColor;
                if (cloudflareWarpStripe27 != null) cloudflareWarpStripe27.Fill = stripeColor;
                if (cloudflareWarpStripe28 != null) cloudflareWarpStripe28.Fill = stripeColor;
                if (cloudflareWarpStripe29 != null) cloudflareWarpStripe29.Fill = stripeColor;
                if (cloudflareWarpStripe30 != null) cloudflareWarpStripe30.Fill = stripeColor;

                Debug.WriteLine("Şerit renkleri güncellendi");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Şerit renkleri güncellenirken hata: {ex.Message}");
            }
        }

        // Overlay yazılarının gölge efektlerini tema moduna göre ayarla
        private void UpdateOverlayShadows(bool isDarkMode)
        {
            try
            {
                var shadowColor = isDarkMode ? Colors.Black : Colors.White;
                var shadowEffect = new DropShadowEffect
                {
                    Color = shadowColor,
                    Direction = 320,
                    ShadowDepth = 3,
                    BlurRadius = 5,
                    Opacity = 0.8
                };

                // Tüm overlay yazılarına gölge efekti uygula
                if (kasperskyText != null) kasperskyText.Effect = shadowEffect;
                if (kasperskyVpnText != null) kasperskyVpnText.Effect = shadowEffect;
                if (cloudflareWarpText != null) cloudflareWarpText.Effect = shadowEffect;

                Debug.WriteLine($"Overlay gölge efektleri güncellendi - Karanlık mod: {isDarkMode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Overlay gölge efektleri güncellenirken hata: {ex.Message}");
            }
        }

        // Mevcut sekmeye göre overlay görünürlüğünü güncelle
        private void UpdateOverlayVisibilityForCurrentTab()
        {
            try
            {
                bool isZapretOrGoodbyeDPITab = (TabControl.SelectedIndex == 2 || TabControl.SelectedIndex == 3); // Zapret = 2, GoodbyeDPI = 3
                
                if (!isZapretOrGoodbyeDPITab)
                {
                    // Diğer sekmelerde overlay'leri gizle
                    kasperskyOverlay.Visibility = Visibility.Collapsed;
                    kasperskyVpnOverlay.Visibility = Visibility.Collapsed;
                    cloudflareWarpOverlay.Visibility = Visibility.Collapsed;
                    Debug.WriteLine("Overlay'ler gizlendi - aktif sekme Zapret/GoodbyeDPI değil");
                }
                else
                {
                    // Zapret veya GoodbyeDPI sekmesinde - tespit edilen overlay'i göster
                    UpdateOverlayVisibility();
                    Debug.WriteLine("Overlay görünürlüğü güncellendi - Zapret/GoodbyeDPI sekmesi aktif");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sekme bazlı overlay güncellemesi sırasında hata: {ex.Message}");
            }
        }

        private async void BtnGoodbyeDPISaveBlacklist_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Önce goodbyedpi.exe işleminin çalışıp çalışmadığını kontrol et
                var goodbyedpiProcesses = Process.GetProcessesByName("goodbyedpi");
                if (goodbyedpiProcesses.Length > 0)
                {
                    System.Windows.MessageBox.Show(
                        "Önce GoodbyeDPI hizmetini kaldırın veya işlemlerini durdurun.",
                        "Uyarı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var blacklistPath = Path.Combine(localGoodbyeDPIPath, "blacklist.txt");
                
                var domains = txtGoodbyeDPIBlacklist.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var cleanDomains = domains.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();
                
                await File.WriteAllLinesAsync(blacklistPath, cleanDomains);
                
                System.Windows.MessageBox.Show("Blacklist başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Blacklist kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnGoodbyeDPIService_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "GoodbyeDPI hizmet kurulumu başlatmak istediğinizden emin misiniz?",
                "GoodbyeDPI Hizmet Kurulumu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            loadingOverlay.Visibility = Visibility.Visible;
            var logPath = GetGoodbyeDPILogPath();
            
            try
            {
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmet kurulumu başlatılıyor...\n");
                
                // Kurulum öncesi temizlik (Discord + tüm hizmetler + Drover dosyaları)
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kurulum öncesi temizlik yapılıyor...\n");
                var cleanupSuccess = await PerformPreSetupCleanupAsync();
                if (cleanupSuccess)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Kurulum öncesi temizlik başarıyla tamamlandı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: Kurulum öncesi temizlik sırasında hata oluştu.\n");
                }
                
                // DNS ayarları yap
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları yapılıyor...\n");
                await SetModernDNSSettingsAsync();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ayarları tamamlandı.\n");
                
                // Hizmet temizleme işlemi
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet temizleme işlemi başlatılıyor...\n");
                await PerformServiceCleanupForGoodbyeDPI();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet temizleme işlemi tamamlandı.\n");
                
                // Dosyalar yoksa kopyala
                if (!CheckGoodbyeDPIFilesExist())
                {
                    // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                    if (AreCriticalWinDivertFilesMissing())
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UYARI: Kritik WinDivert dosyaları eksik olduğu için GoodbyeDPI kurulumu yapılamıyor.\n");
                        System.Windows.MessageBox.Show("Kritik WinDivert dosyaları eksik olduğu için GoodbyeDPI kurulumu yapılamıyor. Lütfen önce gerekli dosyaları ekleyin.", 
                            "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI dosyaları bulunamadı, kopyalanıyor...\n");
                    if (!await EnsureGoodbyeDPIFilesExist())
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: GoodbyeDPI dosyaları kopyalanamadı!\n");
                        return;
                    }
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI dosyaları başarıyla kopyalandı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI dosyaları zaten mevcut.\n");
                }

                // Parametreleri hazırla
                var parameters = txtGoodbyeDPIParams.Text.Trim();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Temel parametreler: {parameters}\n");
                
                if (chkGoodbyeDPIUseBlacklist.IsChecked == true)
                {
                    var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                    var blacklistPath = Path.Combine(localGoodbyeDPIPath, "blacklist.txt");
                    parameters += $" --blacklist \"{blacklistPath}\"";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Blacklist eklendi: {blacklistPath}\n");
                }
                
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Final parametreler: {parameters}\n");

                // Hizmet kurulumu
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulumu başlatılıyor...\n");
                var success = await InstallGoodbyeDPIService(parameters, logPath);
                
                if (success)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulumu başarıyla tamamlandı!\n");
                    System.Windows.MessageBox.Show("GoodbyeDPI hizmet kurulumu başarıyla tamamlandı!", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Başarılı kurulum sonrası kaldır butonunu güncelle
                    CheckGoodbyeDPIRemoveButtonVisibility();
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Hizmet kurulumu başarısız oldu!\n");
                    System.Windows.MessageBox.Show($"GoodbyeDPI hizmet kurulumu başarısız oldu. Lütfen log dosyasını kontrol edin:\n{logPath}", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {ex.Message}\n");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Stack Trace: {ex.StackTrace}\n");
                System.Windows.MessageBox.Show($"GoodbyeDPI hizmet kurulumu sırasında hata oluştu: {ex.Message}\nLog: {logPath}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
                
                // Hizmet durumlarını güncelle
                CheckAllServices();
            }
        }

        private async void BtnGoodbyeDPIBatch_Click(object sender, RoutedEventArgs e)
        {
            // Loading ekranını göster
            loadingOverlay.Visibility = Visibility.Visible;

            try
            {
                // Dosyalar yoksa kopyala
                if (!CheckGoodbyeDPIFilesExist())
                {
                    // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                    if (AreCriticalWinDivertFilesMissing())
                    {
                        System.Windows.MessageBox.Show("Kritik WinDivert dosyaları eksik olduğu için GoodbyeDPI işlemi yapılamıyor. Lütfen önce gerekli dosyaları ekleyin.", 
                            "İşlem Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    if (!await EnsureGoodbyeDPIFilesExist())
                    {
                        return;
                    }
                }

                // DNS ayarları yap
                var logPath = GetGoodbyeDPILogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI Tek Seferlik - DNS ayarları yapılıyor...\n");
                await SetModernDNSSettingsAsync();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI Tek Seferlik - DNS ayarları tamamlandı.\n");

                // Parametreleri hazırla
                var parameters = txtGoodbyeDPIParams.Text.Trim();
                if (chkGoodbyeDPIUseBlacklist.IsChecked == true)
                {
                    var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                    var blacklistPath = Path.Combine(localGoodbyeDPIPath, "blacklist.txt");
                    parameters += $" --blacklist \"{blacklistPath}\"";
                }

                // Tek seferlik çalıştırma
                var success = await RunGoodbyeDPIBatch(parameters);
                
                if (!success)
                {
                    System.Windows.MessageBox.Show("GoodbyeDPI tek seferlik çalıştırma başarısız oldu.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"GoodbyeDPI tek seferlik çalıştırma sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Loading ekranını gizle
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnGoodbyeDPIAdvancedRemove_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "GoodbyeDPI'ı kaldırmak istediğinizden emin misiniz? Bu işlem GoodbyeDPI hizmetini durduracak ve kaldıracaktır.",
                "GoodbyeDPI Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            ShowLoading(true);
            
            try
            {
                await RemoveService("GoodbyeDPI");
            }
            finally
            {
                ShowLoading(false);
                CheckAllServices();
            }
        }

        private async void BtnRemoveGoodbyeDPI_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "GoodbyeDPI'ı kaldırmak istediğinizden emin misiniz? Bu işlem GoodbyeDPI hizmetini durduracak ve kaldıracaktır.",
                "GoodbyeDPI Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            loadingOverlay.Visibility = Visibility.Visible;
            
            try
            {
                await RemoveService("GoodbyeDPI");
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnHelpGoodbyeDPI_Click(object sender, RoutedEventArgs e)
        {
            // Mevcut tema durumunu kontrol et
            bool isDarkMode = btnThemeToggle.IsChecked == true;
            
            var infoWindow = new Window
            {
                Title = "GoodbyeDPI Yardımı - SplitWire-Turkey",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d")) :
                    System.Windows.Media.Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var contentStack = new StackPanel();
            
            var titleText = new TextBlock
            {
                Text = "GoodbyeDPI Kullanımı",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Bold"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            contentStack.Children.Add(titleText);

            // RichTextBox kullanarak formatlı metin oluştur
            var helpText = new System.Windows.Controls.RichTextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Resources/#Poppins Regular"),
                VerticalAlignment = VerticalAlignment.Top,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Metin içeriğini oluştur
            var paragraph = new Paragraph();
            
            // Not - Başlığın hemen altına taşındı
            var noteTitle = new Run("Not: ")
            {
                FontWeight = FontWeights.Bold
            };
            var noteText = new Run("Bu bölümdeki kurulum, sistem geneli çalışır. Hız kaybına sebep olmasa da bazı web site ve uygulamalarda bağlantı sorunlarına yol açabilir. Bu gibi sorunların önüne geçmek için \"Blacklist kullan\" seçeneğini aktifleştirebilirsiniz. Bu kurulumu gerçekleştirdikten sonra sisteminizi her yeniden başlatışınızda ilgili yöntem otomatik olarak çalışmaya başlar.");
            paragraph.Inlines.Add(noteTitle);
            paragraph.Inlines.Add(noteText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Hazır Ayar
            var presetTitle = new Run("Hazır Ayar: ")
            {
                FontWeight = FontWeights.Bold
            };
            var presetText = new Run("GoodbyeDPI için önceden belirlenmiş parametrelerden birini seçer.");
            paragraph.Inlines.Add(presetTitle);
            paragraph.Inlines.Add(presetText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Hazır Ayarı Düzenle
            var editTitle = new Run("Hazır Ayarı Düzenle: ")
            {
                FontWeight = FontWeights.Bold
            };
            var editText = new Run("Seçtiğiniz hazır ayar üzerinde ince ayar ya da değişiklik yapmanızı sağlayan metin kutusunu açar. Bu kutuda düzenleme yaptıktan sonra aşağıdaki butonları kullanarak kutudaki parametreler ile kurulum sağlayabilir ya da tek seferlik çalıştırabilirsiniz.");
            paragraph.Inlines.Add(editTitle);
            paragraph.Inlines.Add(editText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Blacklist Kullan
            var blacklistTitle = new Run("Blacklist Kullan: ")
            {
                FontWeight = FontWeights.Bold
            };
            var blacklistText = new Run("GoodbyeDPI'ı yalnızca tercih edilen domainler için çalıştırır. Varsayılan olarak Discord, Roblox ve Wattpad için blacklist kullanılır.");
            paragraph.Inlines.Add(blacklistTitle);
            paragraph.Inlines.Add(blacklistText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Blacklisti Düzenle
            var editBlacklistTitle = new Run("Blacklisti Düzenle: ")
            {
                FontWeight = FontWeights.Bold
            };
            var editBlacklistText = new Run("GoodbyeDPI'ın üzerinde etkili olacağı domain listesini düzenleyebileceğiniz metin kutusunu açar. Düzenlemeyi yaptıktan sonra Kaydet butonuna basarak değişiklikleri kaydedebilirsiniz.");
            paragraph.Inlines.Add(editBlacklistTitle);
            paragraph.Inlines.Add(editBlacklistText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Hizmet Kur
            var serviceTitle = new Run("Hizmet Kur: ")
            {
                FontWeight = FontWeights.Bold
            };
            var serviceText = new Run("Üst kısımda belirttiğiniz tercihlere göre (Hazır ayar ve blacklist tercihleri) GoodbyeDPI hizmetini kurar.");
            paragraph.Inlines.Add(serviceTitle);
            paragraph.Inlines.Add(serviceText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Tek Seferlik
            var onceTitle = new Run("Tek Seferlik: ")
            {
                FontWeight = FontWeights.Bold
            };
            var onceText = new Run("Üst kısımda belirttiğiniz tercihlere göre (Hazır ayar ve blacklist tercihleri) GoodbyeDPI'ı tek seferlik çalıştırır. Açılan konsol penceresini kapattığınızda GoodbyeDPI çalışmayı durdurur.");
            paragraph.Inlines.Add(onceTitle);
            paragraph.Inlines.Add(onceText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // GoodbyeDPI'ı Kaldır
            var removeTitle = new Run("GoodbyeDPI'ı Kaldır: ")
            {
                FontWeight = FontWeights.Bold
            };
            var removeText = new Run("GoodbyeDPI'ı kaldırır.");
            paragraph.Inlines.Add(removeTitle);
            paragraph.Inlines.Add(removeText);
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new LineBreak());

            // Not 2
            var note2Title = new Run("Not 2: ")
            {
                FontWeight = FontWeights.Bold
            };
            var note2Text = new Run("Eğer Discord uygulaması Checking for updates… ekranında kalırsa modeminizi kapatıp 15 saniye bekledikten sonra tekrar açın ve ardından bilgisayarınızı yeniden başlatın.");
            paragraph.Inlines.Add(note2Title);
            paragraph.Inlines.Add(note2Text);

            helpText.Document = new FlowDocument(paragraph);
            
            // RichTextBox tema renklerini ayarla
            if (isDarkMode)
            {
                helpText.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                helpText.Foreground = System.Windows.Media.Brushes.Black;
            }
            
            contentStack.Children.Add(helpText);

            scrollViewer.Content = contentStack;
            mainGrid.Children.Add(scrollViewer);
            Grid.SetRow(scrollViewer, 1);

            // Kapat butonu
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = isDarkMode ? 
                    new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#373738")) :
                    System.Windows.Media.Brushes.LightGray,
                Foreground = isDarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black
            };
            closeButton.Click += (s, args) => infoWindow.Close();
            Grid.SetRow(closeButton, 2);
            mainGrid.Children.Add(closeButton);

            infoWindow.Content = mainGrid;
            infoWindow.ShowDialog();
        }

        private void LoadGoodbyeDPIPresets()
        {
            try
            {
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var presetsPath = Path.Combine(localGoodbyeDPIPath, "presets.txt");
                
                if (File.Exists(presetsPath))
                {
                    var lines = File.ReadAllLines(presetsPath);
                    cmbGoodbyeDPIPresets.Items.Clear();
                    
                    foreach (var line in lines)
                    {
                        if (line.Contains(":"))
                        {
                            var presetName = line.Split(':')[0];
                            cmbGoodbyeDPIPresets.Items.Add(new ComboBoxItem { Content = presetName });
                        }
                    }
                    
                    if (cmbGoodbyeDPIPresets.Items.Count > 0)
                    {
                        cmbGoodbyeDPIPresets.SelectedIndex = 0;
                    }
                }
                else
                {
                    // Varsayılan preset'leri ekle
                    var defaultPresets = new[] { "Standart", "Alternatif", "Alternatif 2", "Alternatif 3" };
                    foreach (var preset in defaultPresets)
                    {
                        cmbGoodbyeDPIPresets.Items.Add(new ComboBoxItem { Content = preset });
                    }
                    
                    if (cmbGoodbyeDPIPresets.Items.Count > 0)
                    {
                        cmbGoodbyeDPIPresets.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GoodbyeDPI preset'leri yüklenirken hata: {ex.Message}");
                
                // Hata durumunda varsayılan preset'leri ekle
                var defaultPresets = new[] { "Standart", "Alternatif", "Alternatif 2", "Alternatif 3" };
                foreach (var preset in defaultPresets)
                {
                    cmbGoodbyeDPIPresets.Items.Add(new ComboBoxItem { Content = preset });
                }
                
                if (cmbGoodbyeDPIPresets.Items.Count > 0)
                {
                    cmbGoodbyeDPIPresets.SelectedIndex = 0;
                }
            }
        }

        private string GetGoodbyeDPIPresetParameters(string presetName)
        {
            try
            {
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var presetsPath = Path.Combine(localGoodbyeDPIPath, "presets.txt");
                
                if (File.Exists(presetsPath))
                {
                    var lines = File.ReadAllLines(presetsPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith(presetName + ":"))
                        {
                            return line.Substring(presetName.Length + 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GoodbyeDPI preset parametreleri alınırken hata: {ex.Message}");
            }
            
            return string.Empty;
        }

        private void LoadGoodbyeDPIBlacklist()
        {
            try
            {
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var blacklistPath = Path.Combine(localGoodbyeDPIPath, "blacklist.txt");
                
                if (File.Exists(blacklistPath))
                {
                    var content = File.ReadAllText(blacklistPath);
                    txtGoodbyeDPIBlacklist.Text = content;
                }
                else
                {
                    txtGoodbyeDPIBlacklist.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GoodbyeDPI blacklist yüklenirken hata: {ex.Message}");
                txtGoodbyeDPIBlacklist.Text = string.Empty;
            }
        }

        private bool CheckGoodbyeDPIFilesExist()
        {
            try
            {
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var requiredFiles = new[]
                {
                    "service_template.cmd",
                    "batch_template.cmd",
                    "presets.txt",
                    "blacklist.txt"
                };

                // Temel dosyaları kontrol et
                foreach (var file in requiredFiles)
                {
                    var filePath = Path.Combine(localGoodbyeDPIPath, file);
                    if (!File.Exists(filePath))
                    {
                        return false;
                    }
                }

                // x86_64 klasöründeki WinDivert dosyalarını kontrol et
                var x64WinDivertDllPath = Path.Combine(localGoodbyeDPIPath, "x86_64", "WinDivert.dll");
                var x64WinDivertSysPath = Path.Combine(localGoodbyeDPIPath, "x86_64", "WinDivert64.sys");
                
                if (!File.Exists(x64WinDivertDllPath) || !File.Exists(x64WinDivertSysPath))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> EnsureGoodbyeDPIFilesExist()
        {
            try
            {
                // Kritik WinDivert dosyaları eksikse kopyalama işlemini başlatma
                if (AreCriticalWinDivertFilesMissing())
                {
                    Debug.WriteLine("Kritik WinDivert dosyaları eksik - GoodbyeDPI dosyaları LocalAppData'ya kopyalanmıyor");
                    return false;
                }
                
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                
                if (!Directory.Exists(localGoodbyeDPIPath))
                {
                    Directory.CreateDirectory(localGoodbyeDPIPath);
                }

                var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi");
                
                if (!Directory.Exists(sourcePath))
                {
                    System.Windows.MessageBox.Show("GoodbyeDPI kaynak dosyaları bulunamadı. Lütfen programı yeniden yükleyin.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Dosyaları kopyala
                var filesToCopy = new[] { "service_template.cmd", "batch_template.cmd", "presets.txt", "blacklist.txt" };
                
                foreach (var file in filesToCopy)
                {
                    var sourceFile = Path.Combine(sourcePath, file);
                    var destFile = Path.Combine(localGoodbyeDPIPath, file);
                    
                    if (File.Exists(sourceFile))
                    {
                        File.Copy(sourceFile, destFile, true);
                    }
                }

                // x86 ve x86_64 klasörlerini kopyala
                var archFolders = new[] { "x86", "x86_64" };
                foreach (var folder in archFolders)
                {
                    var sourceFolder = Path.Combine(sourcePath, folder);
                    var destFolder = Path.Combine(localGoodbyeDPIPath, folder);
                    
                    if (Directory.Exists(sourceFolder))
                    {
                        if (Directory.Exists(destFolder))
                        {
                            Directory.Delete(destFolder, true);
                        }
                        await Task.Run(() => CopyDirectory(sourceFolder, destFolder));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"GoodbyeDPI dosyaları kopyalanırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<bool> InstallGoodbyeDPIService(string parameters, string logPath = null)
        {
            try
            {
                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] InstallGoodbyeDPIService başlatılıyor...\n");
                
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var serviceTemplatePath = Path.Combine(localGoodbyeDPIPath, "service_template.cmd");
                
                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI yolu: {localGoodbyeDPIPath}\n");
                
                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Service template yolu: {serviceTemplatePath}\n");
                
                if (!File.Exists(serviceTemplatePath))
                {
                    if (logPath != null)
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Service template dosyası bulunamadı!\n");
                    
                    System.Windows.MessageBox.Show("GoodbyeDPI service template dosyası bulunamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Template dosyasını oku ve parametreleri değiştir
                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Template dosyası okunuyor...\n");
                
                var templateContent = await File.ReadAllTextAsync(serviceTemplatePath);
                var modifiedContent = templateContent.Replace("*parameters*", parameters);
                
                if (logPath != null)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Template içeriği:\n{templateContent}\n");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Değiştirilmiş içerik:\n{modifiedContent}\n");
                }
                
                // Geçici dosya oluştur
                var tempServicePath = Path.Combine(localGoodbyeDPIPath, "temp_service.cmd");
                await File.WriteAllTextAsync(tempServicePath, modifiedContent);
                
                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Geçici script dosyası oluşturuldu: {tempServicePath}\n");

                // Hizmet kurulum script'ini çalıştır
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{tempServicePath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = localGoodbyeDPIPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Script çalıştırılıyor: {psi.Arguments}\n");

                using var process = Process.Start(psi);
                if (process != null)
                {
                    // Çıkışları oku
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    if (logPath != null)
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Process çıkış kodu: {process.ExitCode}\n");
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Standard Output:\n{output}\n");
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Standard Error:\n{error}\n");
                    }
                    
                    // Geçici dosyayı sil
                    try
                    {
                        File.Delete(tempServicePath);
                        if (logPath != null)
                            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Geçici dosya silindi.\n");
                    }
                    catch (Exception ex)
                    {
                        if (logPath != null)
                            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Geçici dosya silinemedi: {ex.Message}\n");
                    }
                    
                    // Hizmet durumunu kontrol et
                    if (logPath != null)
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet durumu kontrol ediliyor...\n");
                    
                    var serviceInstalled = await CheckServiceInstalled("GoodbyeDPI");
                    if (logPath != null)
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hizmet kurulu mu: {serviceInstalled}\n");
                    
                    return serviceInstalled && process.ExitCode == 0;
                }

                if (logPath != null)
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: Process başlatılamadı!\n");
                
                return false;
            }
            catch (Exception ex)
            {
                if (logPath != null)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HATA: {ex.Message}\n");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Stack Trace: {ex.StackTrace}\n");
                }
                
                Debug.WriteLine($"GoodbyeDPI hizmet kurulumu hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunGoodbyeDPIBatch(string parameters)
        {
            try
            {
                var localGoodbyeDPIPath = GetLocalAppDataGoodbyeDPIPath();
                var batchTemplatePath = Path.Combine(localGoodbyeDPIPath, "batch_template.cmd");
                
                if (!File.Exists(batchTemplatePath))
                {
                    System.Windows.MessageBox.Show("GoodbyeDPI batch template dosyası bulunamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Template dosyasını oku ve parametreleri değiştir
                var templateContent = await File.ReadAllTextAsync(batchTemplatePath);
                var modifiedContent = templateContent.Replace("*parameters*", parameters);
                
                // Geçici dosya oluştur
                var tempBatchPath = Path.Combine(localGoodbyeDPIPath, "temp_batch.cmd");
                await File.WriteAllTextAsync(tempBatchPath, modifiedContent);

                // Batch script'ini çalıştır
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{tempBatchPath}\"\"",
                    UseShellExecute = true,
                    WorkingDirectory = localGoodbyeDPIPath
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    // Tek seferlik çalıştırma olduğu için process'i bekleme
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GoodbyeDPI batch çalıştırma hatası: {ex.Message}");
                return false;
            }
        }

        private async Task PerformServiceCleanupForGoodbyeDPI()
        {
            try
            {
                var logPath = GetGoodbyeDPILogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmet temizliği başlatılıyor...\n");
                
                // Mevcut GoodbyeDPI hizmetini durdur ve kaldır
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmeti durduruluyor...\n");
                ExecuteCommand("sc", "stop GoodbyeDPI");
                await Task.Delay(2000);
                
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmeti kaldırılıyor...\n");
                ExecuteCommand("sc", "delete GoodbyeDPI");
                await Task.Delay(1000);
                
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmet temizliği tamamlandı.\n");
            }
            catch (Exception ex)
            {
                var logPath = GetGoodbyeDPILogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GoodbyeDPI hizmet temizliği hatası: {ex.Message}\n");
                Debug.WriteLine($"GoodbyeDPI hizmet temizliği hatası: {ex.Message}");
            }
        }

        private string GetLocalAppDataGoodbyeDPIPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "SplitWire-Turkey", "GoodbyeDPI");
        }

        private async Task<bool> CheckServiceInstalled(string serviceName)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"query {serviceName}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output.Contains("SERVICE_NAME:") && !output.Contains("1060");
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Theme Management

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (btnThemeToggle.IsChecked == true)
                {
                    // Karanlık mod
                    ApplyDarkTheme();
                }
                else
                {
                    // Aydınlık mod
                    ApplyLightTheme();
                }
                
                // Overlay'leri güncelle
                UpdateOverlayTheme();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Theme toggle hatası: {ex.Message}");
            }
        }

                private void ApplyDarkTheme()
        {
            try
            {
                var animationDuration = TimeSpan.FromMilliseconds(500);
                var darkColor = (Color)ColorConverter.ConvertFromString("#1c1c1d");

                // Görev çubuğunu karanlık moda al
                SetTaskbarDarkMode(true);
 
                // Ana pencere arkaplan rengi - animasyonlu
                AnimateBackgroundColor(this, darkColor, animationDuration);
                
                // TabControl arkaplan rengi - animasyonlu
                AnimateBackgroundColor(TabControl, darkColor, animationDuration);
                
                // TabItem arkaplan rengi
                var tabItemStyle = new Style(typeof(TabItem), TabControl.Resources[typeof(TabItem)] as Style);
                tabItemStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d"))));
                tabItemStyle.Setters.Add(new Setter(TabItem.ForegroundProperty, System.Windows.Media.Brushes.White));
                
                // Header arkaplan rengi - animasyonlu
                var headerCard = this.FindName("headerCard") as System.Windows.Controls.Border;
                if (headerCard != null)
                {
                    var headerColor = (Color)ColorConverter.ConvertFromString("#252728");
                    AnimateBackgroundColor(headerCard, headerColor, animationDuration);
                }
                
                // Logo ve yazı PNG'lerine inverted hallerini uygula
                var imgLogo = this.FindName("imgLogo") as System.Windows.Controls.Image;
                var imgText = this.FindName("imgText") as System.Windows.Controls.Image;
                if (imgLogo != null)
                {
                    imgLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/splitwire-logo-128_inverted.png"));
                }
                if (imgText != null)
                {
                    imgText.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/splitwireturkeytext_inverted.png"));
                }
                
                // Ana Sayfa ve ByeDPI butonları MaterialDesignOutlinedButton stilini korur
                // Özel arkaplan rengi verilmez - outline tasarım korunur
                
                // Kaspersky overlay renklerini güncelle
                UpdateKasperskyOverlayColors(true);
                
                // Hakkında ve yardım butonları arkaplan rengi - animasyonlu
                var btnInfo = this.FindName("btnInfo") as System.Windows.Controls.Button;
                var infoButtonColor = (Color)ColorConverter.ConvertFromString("#3f3f40");
                
                if (btnInfo != null)
                {
                    AnimateBackgroundColor(btnInfo, infoButtonColor, animationDuration);
                    AnimateForegroundColor(btnInfo, Colors.White, animationDuration);
                }
                
                // Yardım butonları stilini karanlık moda güncelle
                var btnHelpMainPage = this.FindName("btnHelpMainPage") as System.Windows.Controls.Button;
                var btnHelpByeDPI = this.FindName("btnHelpByeDPI") as System.Windows.Controls.Button;
                var btnHelpZapret = this.FindName("btnHelpZapret") as System.Windows.Controls.Button;
                var btnHelpGoodbyeDPI = this.FindName("btnHelpGoodbyeDPI") as System.Windows.Controls.Button;
                var btnHelpAdvanced = this.FindName("btnHelpAdvanced") as System.Windows.Controls.Button;
                
                // Stil referansını farklı yollarla deneyelim
                var darkStyle = System.Windows.Application.Current.Resources["InfoButtonStyleDark"] as Style;
                if (darkStyle == null)
                {
                    darkStyle = this.Resources["InfoButtonStyleDark"] as Style;
                }
                if (darkStyle == null)
                {
                    darkStyle = this.FindResource("InfoButtonStyleDark") as Style;
                }
                
                if (darkStyle != null)
                {
                    if (btnHelpMainPage != null) 
                    {
                        btnHelpMainPage.Style = darkStyle;
                        Debug.WriteLine("btnHelpMainPage karanlık mod stili uygulandı");
                    }
                    if (btnHelpByeDPI != null) 
                    {
                        btnHelpByeDPI.Style = darkStyle;
                        Debug.WriteLine("btnHelpByeDPI karanlık mod stili uygulandı");
                    }
                    if (btnHelpZapret != null) 
                    {
                        btnHelpZapret.Style = darkStyle;
                        Debug.WriteLine("btnHelpZapret karanlık mod stili uygulandı");
                    }
                    if (btnHelpGoodbyeDPI != null) 
                    {
                        btnHelpGoodbyeDPI.Style = darkStyle;
                        Debug.WriteLine("btnHelpGoodbyeDPI karanlık mod stili uygulandı");
                    }
                    if (btnHelpAdvanced != null) 
                    {
                        btnHelpAdvanced.Style = darkStyle;
                        Debug.WriteLine("btnHelpAdvanced karanlık mod stili uygulandı");
                    }
                }
                else
                {
                    Debug.WriteLine("InfoButtonStyleDark stili hiçbir yoldan bulunamadı!");
                    // Alternatif olarak doğrudan renk değişikliği yapalım - animasyonlu
                    if (btnHelpMainPage != null) 
                    {
                        AnimateBackgroundColor(btnHelpMainPage, infoButtonColor, animationDuration);
                        AnimateForegroundColor(btnHelpMainPage, Colors.White, animationDuration);
                    }
                    if (btnHelpByeDPI != null) 
                    {
                        AnimateBackgroundColor(btnHelpByeDPI, infoButtonColor, animationDuration);
                        AnimateForegroundColor(btnHelpByeDPI, Colors.White, animationDuration);
                    }
                    if (btnHelpZapret != null) 
                    {
                        AnimateBackgroundColor(btnHelpZapret, infoButtonColor, animationDuration);
                        AnimateForegroundColor(btnHelpZapret, Colors.White, animationDuration);
                    }
                    if (btnHelpGoodbyeDPI != null) 
                    {
                        AnimateBackgroundColor(btnHelpGoodbyeDPI, infoButtonColor, animationDuration);
                        AnimateForegroundColor(btnHelpGoodbyeDPI, Colors.White, animationDuration);
                    }
                    if (btnHelpAdvanced != null) 
                    {
                        AnimateBackgroundColor(btnHelpAdvanced, infoButtonColor, animationDuration);
                        AnimateForegroundColor(btnHelpAdvanced, Colors.White, animationDuration);
                    }
                }
                
                // Toggle switch stillerini karanlık moda güncelle
                UpdateToggleSwitchStyles(true);
                
                // Yükleme ekranı karanlık mod - animasyonlu
                var loadingCard = this.FindName("loadingCard") as MaterialDesignThemes.Wpf.Card;
                var loadingText = this.FindName("loadingText") as System.Windows.Controls.TextBlock;
                var loadingProgressBar = this.FindName("loadingProgressBar") as System.Windows.Controls.ProgressBar;
                
                if (loadingCard != null)
                {
                    AnimateBackgroundColor(loadingCard, Colors.Black, animationDuration);
                }
                if (loadingText != null)
                {
                    AnimateForegroundColor(loadingText, Colors.White, animationDuration);
                }
                if (loadingProgressBar != null)
                {
                    AnimateForegroundColor(loadingProgressBar, Colors.White, animationDuration);
                }
                
                // Diğer UI elementleri için karanlık tema
                System.Windows.Application.Current.Resources["MaterialDesignPaper"] = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1c1c1d"));
                System.Windows.Application.Current.Resources["MaterialDesignBody"] = System.Windows.Media.Brushes.White;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dark theme uygulama hatası: {ex.Message}");
            }
        }

        private void ApplyLightTheme()
        {
            try
            {
                var animationDuration = TimeSpan.FromMilliseconds(500);

                // Görev çubuğunu aydınlık moda al
                SetTaskbarDarkMode(false);
                
                // Ana pencere arkaplan rengi - animasyonlu
                AnimateBackgroundColor(this, Colors.White, animationDuration);
                
                // TabControl arkaplan rengi - animasyonlu
                AnimateBackgroundColor(TabControl, Colors.White, animationDuration);
                
                // TabItem arkaplan rengi
                var tabItemStyle = new Style(typeof(TabItem), TabControl.Resources[typeof(TabItem)] as Style);
                tabItemStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, System.Windows.Media.Brushes.White));
                tabItemStyle.Setters.Add(new Setter(TabItem.ForegroundProperty, System.Windows.Media.Brushes.Black));
                
                // Header arkaplan rengi (varsayılan) - animasyonlu
                var headerCard = this.FindName("headerCard") as System.Windows.Controls.Border;
                if (headerCard != null)
                {
                    AnimateBackgroundColor(headerCard, Colors.White, animationDuration);
                }
                
                // Logo ve yazı PNG'lerini normal haline döndür
                var imgLogo = this.FindName("imgLogo") as System.Windows.Controls.Image;
                var imgText = this.FindName("imgText") as System.Windows.Controls.Image;
                if (imgLogo != null)
                {
                    imgLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/splitwire-logo-128.png"));
                }
                if (imgText != null)
                {
                    imgText.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/splitwireturkeytext.png"));
                }
                
                // Ana Sayfa ve ByeDPI butonları MaterialDesignOutlinedButton stilini korur
                // Özel arkaplan rengi verilmez - outline tasarım korunur
                
                // Hakkında ve yardım butonları arkaplan rengi (varsayılan) - animasyonlu
                var btnInfo = this.FindName("btnInfo") as System.Windows.Controls.Button;
                var defaultInfoColor = (Color)ColorConverter.ConvertFromString("#E0E0E0");
                var defaultInfoTextColor = (Color)ColorConverter.ConvertFromString("#424242");
                
                if (btnInfo != null)
                {
                    AnimateBackgroundColor(btnInfo, defaultInfoColor, animationDuration);
                    AnimateForegroundColor(btnInfo, defaultInfoTextColor, animationDuration);
                }
                
                // Kaspersky overlay renklerini güncelle
                UpdateKasperskyOverlayColors(false);
                
                // Yardım butonları stilini aydınlık moda güncelle
                var btnHelpMainPage = this.FindName("btnHelpMainPage") as System.Windows.Controls.Button;
                var btnHelpByeDPI = this.FindName("btnHelpByeDPI") as System.Windows.Controls.Button;
                var btnHelpZapret = this.FindName("btnHelpZapret") as System.Windows.Controls.Button;
                var btnHelpGoodbyeDPI = this.FindName("btnHelpGoodbyeDPI") as System.Windows.Controls.Button;
                var btnHelpAdvanced = this.FindName("btnHelpAdvanced") as System.Windows.Controls.Button;
                
                // Stil referansını farklı yollarla deneyelim
                var lightStyle = System.Windows.Application.Current.Resources["InfoButtonStyle"] as Style;
                if (lightStyle == null)
                {
                    lightStyle = this.Resources["InfoButtonStyle"] as Style;
                }
                if (lightStyle == null)
                {
                    lightStyle = this.FindResource("InfoButtonStyle") as Style;
                }
                
                if (lightStyle != null)
                {
                    if (btnHelpMainPage != null) 
                    {
                        btnHelpMainPage.Style = lightStyle;
                        Debug.WriteLine("btnHelpMainPage aydınlık mod stili uygulandı");
                    }
                    if (btnHelpByeDPI != null) 
                    {
                        btnHelpByeDPI.Style = lightStyle;
                        Debug.WriteLine("btnHelpByeDPI aydınlık mod stili uygulandı");
                    }
                    if (btnHelpZapret != null) 
                    {
                        btnHelpZapret.Style = lightStyle;
                        Debug.WriteLine("btnHelpZapret aydınlık mod stili uygulandı");
                    }
                    if (btnHelpGoodbyeDPI != null) 
                    {
                        btnHelpGoodbyeDPI.Style = lightStyle;
                        Debug.WriteLine("btnHelpGoodbyeDPI aydınlık mod stili uygulandı");
                    }
                    if (btnHelpAdvanced != null) 
                    {
                        btnHelpAdvanced.Style = lightStyle;
                        Debug.WriteLine("btnHelpAdvanced aydınlık mod stili uygulandı");
                    }
                }
                else
                {
                    Debug.WriteLine("InfoButtonStyle stili hiçbir yoldan bulunamadı!");
                    // Alternatif olarak doğrudan renk değişikliği yapalım - animasyonlu
                    if (btnHelpMainPage != null) 
                    {
                        AnimateBackgroundColor(btnHelpMainPage, defaultInfoColor, animationDuration);
                        AnimateForegroundColor(btnHelpMainPage, defaultInfoTextColor, animationDuration);
                    }
                    if (btnHelpByeDPI != null) 
                    {
                        AnimateBackgroundColor(btnHelpByeDPI, defaultInfoColor, animationDuration);
                        AnimateForegroundColor(btnHelpByeDPI, defaultInfoTextColor, animationDuration);
                    }
                    if (btnHelpZapret != null) 
                    {
                        AnimateBackgroundColor(btnHelpZapret, defaultInfoColor, animationDuration);
                        AnimateForegroundColor(btnHelpZapret, defaultInfoTextColor, animationDuration);
                    }
                    if (btnHelpGoodbyeDPI != null) 
                    {
                        AnimateBackgroundColor(btnHelpGoodbyeDPI, defaultInfoColor, animationDuration);
                        AnimateForegroundColor(btnHelpGoodbyeDPI, defaultInfoTextColor, animationDuration);
                    }
                    if (btnHelpAdvanced != null) 
                    {
                        AnimateBackgroundColor(btnHelpAdvanced, defaultInfoColor, animationDuration);
                        AnimateForegroundColor(btnHelpAdvanced, defaultInfoTextColor, animationDuration);
                    }
                }
                
                // Toggle switch stillerini aydınlık moda güncelle
                UpdateToggleSwitchStyles(false);
                
                // Yükleme ekranı aydınlık mod - animasyonlu
                var loadingCard = this.FindName("loadingCard") as MaterialDesignThemes.Wpf.Card;
                var loadingText = this.FindName("loadingText") as System.Windows.Controls.TextBlock;
                var loadingProgressBar = this.FindName("loadingProgressBar") as System.Windows.Controls.ProgressBar;
                
                if (loadingCard != null)
                {
                    AnimateBackgroundColor(loadingCard, Colors.White, animationDuration);
                }
                if (loadingText != null)
                {
                    AnimateForegroundColor(loadingText, Colors.Black, animationDuration);
                }
                if (loadingProgressBar != null)
                {
                    // Progress bar için varsayılan rengi temizle
                    var defaultProgressColor = (Color)ColorConverter.ConvertFromString("#2196F3"); // Material Design primary blue
                    AnimateForegroundColor(loadingProgressBar, defaultProgressColor, animationDuration);
                }
                
                // Diğer UI elementleri için aydınlık tema
                System.Windows.Application.Current.Resources["MaterialDesignPaper"] = System.Windows.Media.Brushes.White;
                System.Windows.Application.Current.Resources["MaterialDesignBody"] = System.Windows.Media.Brushes.Black;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Light theme uygulama hatası: {ex.Message}");
            }
        }

        #endregion

        // Animasyon yardımcı metotları
        private void AnimateColorProperty(DependencyObject target, DependencyProperty property, Color fromColor, Color toColor, TimeSpan duration)
        {
            var animation = new ColorAnimation
            {
                From = fromColor,
                To = toColor,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var brush = target.GetValue(property) as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(fromColor);
                target.SetValue(property, brush);
            }

            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void AnimateBackgroundColor(FrameworkElement element, Color toColor, TimeSpan duration)
        {
            if (element == null) return;

            Brush currentBrush = null;
            if (element is System.Windows.Controls.Control control)
            {
                currentBrush = control.Background;
            }
            else if (element is System.Windows.Controls.Panel panel)
            {
                currentBrush = panel.Background;
            }
            else if (element is Border border)
            {
                currentBrush = border.Background;
            }
            else if (element is Window window)
            {
                currentBrush = window.Background;
            }

            var fromColor = (currentBrush as SolidColorBrush)?.Color ?? Colors.White;
            var newBrush = new SolidColorBrush(fromColor);

            if (element is System.Windows.Controls.Control ctrl)
            {
                ctrl.Background = newBrush;
            }
            else if (element is System.Windows.Controls.Panel pnl)
            {
                pnl.Background = newBrush;
            }
            else if (element is Border brd)
            {
                brd.Background = newBrush;
            }
            else if (element is Window wnd)
            {
                wnd.Background = newBrush;
            }

            var animation = new ColorAnimation
            {
                From = fromColor,
                To = toColor,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            newBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void AnimateForegroundColor(FrameworkElement element, Color toColor, TimeSpan duration)
        {
            if (element == null) return;

            Brush currentBrush = null;
            if (element is System.Windows.Controls.Control control)
            {
                currentBrush = control.Foreground;
            }
            else if (element is TextBlock textBlock)
            {
                currentBrush = textBlock.Foreground;
            }

            var fromColor = (currentBrush as SolidColorBrush)?.Color ?? Colors.Black;
            var newBrush = new SolidColorBrush(fromColor);

            if (element is System.Windows.Controls.Control ctrl)
            {
                ctrl.Foreground = newBrush;
            }
            else if (element is TextBlock txtBlk)
            {
                txtBlk.Foreground = newBrush;
            }

            var animation = new ColorAnimation
            {
                From = fromColor,
                To = toColor,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            newBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        // Pencere boyutu animasyon metotları
        private void AnimateWindowSize(double targetWidth, double targetHeight, TimeSpan duration)
        {
            var widthAnimation = new DoubleAnimation
            {
                From = this.Width,
                To = targetWidth,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var heightAnimation = new DoubleAnimation
            {
                From = this.Height,
                To = targetHeight,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // Width ve Height animasyonlarını paralel olarak başlat
            this.BeginAnimation(WidthProperty, widthAnimation);
            this.BeginAnimation(HeightProperty, heightAnimation);
        }

        private void AnimateWindowWidth(double targetWidth, TimeSpan duration)
        {
            var widthAnimation = new DoubleAnimation
            {
                From = this.Width,
                To = targetWidth,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(WidthProperty, widthAnimation);
        }

        private void AnimateWindowHeight(double targetHeight, TimeSpan duration)
        {
            // Önceki animasyonları durdur
            this.BeginAnimation(HeightProperty, null);
            
            var heightAnimation = new DoubleAnimation
            {
                From = this.Height,
                To = targetHeight,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(HeightProperty, heightAnimation);
        }

        // Tarayıcı uygulamalarını döndüren yardımcı metot
        private string GetBrowserApplications()
        {
            var browsers = new[]
            {
                "browser.exe",
                "chrome.exe",
                "firefox.exe",
                "opera.exe",
                "operagx.exe",
                "brave.exe",
                "vivaldi.exe",
                "msedge.exe"
            };
            
            return string.Join(" ", browsers);
        }

        // WireSock kısayol silme yardımcı metodu
        private void RemoveWireSockShortcut(string logPath)
        {
            try
            {
                // Public Desktop konumundaki kısayolu sil
                var publicDesktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                var publicShortcutPath = Path.Combine(publicDesktopPath, "WireSock Secure Connect.lnk");
                
                if (File.Exists(publicShortcutPath))
                {
                    File.Delete(publicShortcutPath);
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Public Desktop'taki WireSock kısayolu silindi: {publicShortcutPath}\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Public Desktop'ta WireSock kısayolu bulunamadı: {publicShortcutPath}\n");
                }

                // Normal Desktop konumundaki kısayolu sil
                var userDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var userShortcutPath = Path.Combine(userDesktopPath, "WireSock Secure Connect.lnk");
                
                if (File.Exists(userShortcutPath))
                {
                    File.Delete(userShortcutPath);
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] User Desktop'taki WireSock kısayolu silindi: {userShortcutPath}\n");
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] User Desktop'ta WireSock kısayolu bulunamadı: {userShortcutPath}\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WireSock kısayol silme hatası: {ex.Message}\n");
            }
        }

        private void UpdateToggleSwitchStyles(bool isDarkMode)
        {
            try
            {
                var styleKey = isDarkMode ? "ModernToggleSwitchStyleDark" : "ModernToggleSwitchStyle";
                var style = this.Resources[styleKey] as Style;
                
                if (style != null)
                {
                    // Tüm toggle switch'leri bul ve stilini güncelle
                    var toggleSwitches = new[]
                    {
                        "chkAdvancedSettings",
                        "chkBrowserTunneling",
                        "chkManualParams",
                        "chkGoodbyeDPIManualParams",
                        "chkGoodbyeDPIUseBlacklist",
                        "chkGoodbyeDPIEditBlacklist",
                        "chkByeDPIBrowserTunneling" // Added for dark mode support
                    };
                    
                    foreach (var switchName in toggleSwitches)
                    {
                        var toggleSwitch = this.FindName(switchName) as ToggleButton;
                        if (toggleSwitch != null)
                        {
                            toggleSwitch.Style = style;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Toggle switch stil güncelleme hatası: {ex.Message}");
            }
        }

        // ByeDPI sayfası yüklendiğinde UI durumunu güncelleyen metot
        private void UpdateByeDPIUIState()
        {
            try
            {
                // ProxiFyreService'in yüklü olup olmadığını kontrol et
                bool proxiFyreInstalled = IsServiceInstalled("ProxiFyreService");
                
                Debug.WriteLine($"UpdateByeDPIUIState: ProxiFyreService yüklü: {proxiFyreInstalled}");
                
                if (proxiFyreInstalled)
                {
                    // ProxiFyreService yüklü, switch'i gizle ve label'ı göster
                    if (byedpiBrowserTunnelingPanel != null)
                    {
                        byedpiBrowserTunnelingPanel.Visibility = Visibility.Collapsed;
                        Debug.WriteLine("Switch gizlendi - ProxiFyreService yüklü");
                    }
                    
                    // app-config.json dosyasında chrome var mı kontrol et
                    bool hasChrome = CheckProxiFyreHasChrome();
                    Debug.WriteLine($"Chrome kontrol: {hasChrome}");
                    
                    if (lblByeDPIBrowserTunnelingStatus != null)
                    {
                        if (hasChrome)
                        {
                            lblByeDPIBrowserTunnelingStatus.Text = "ByeDPI Split Tunneling tarayıcılar için de tünelleme yapıyor.\nDeğiştirmek için önce ByeDPI'ı kaldırın.";
                            lblByeDPIBrowserTunnelingStatus.Foreground = new SolidColorBrush(Colors.Green);
                        }
                        else
                        {
                            lblByeDPIBrowserTunnelingStatus.Text = "ByeDPI Split Tunneling tarayıcılar için tünelleme yapmıyor.\nDeğiştirmek için önce ByeDPI'ı kaldırın.";
                            lblByeDPIBrowserTunnelingStatus.Foreground = new SolidColorBrush(Colors.Red);
                        }
                        
                        lblByeDPIBrowserTunnelingStatus.Visibility = Visibility.Visible;
                        Debug.WriteLine("Label gösterildi");
                    }
                }
                else
                {
                    // ProxiFyreService yüklü değil, switch'i göster ve label'ı gizle
                    if (byedpiBrowserTunnelingPanel != null)
                    {
                        byedpiBrowserTunnelingPanel.Visibility = Visibility.Visible;
                        Debug.WriteLine("Switch gösterildi - ProxiFyreService yüklü değil");
                    }
                    
                    if (lblByeDPIBrowserTunnelingStatus != null)
                    {
                        lblByeDPIBrowserTunnelingStatus.Visibility = Visibility.Collapsed;
                        Debug.WriteLine("Label gizlendi");
                    }
                    
                    // Switch'i kapalı yap
                    if (chkByeDPIBrowserTunneling != null)
                    {
                        chkByeDPIBrowserTunneling.IsChecked = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ByeDPI UI durum güncelleme hatası: {ex.Message}");
            }
        }

        // app-config.json dosyasında chrome var mı kontrol eden metot
        private bool CheckProxiFyreHasChrome()
        {
            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var configPath = Path.Combine(currentDir, "res", "proxifyre", "app-config.json");
                
                if (!File.Exists(configPath))
                {
                    return false;
                }
                
                var jsonContent = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<ProxiFyreConfig>(jsonContent);
                
                if (config?.proxies != null)
                {
                    foreach (var proxy in config.proxies)
                    {
                        if (proxy.appNames != null && proxy.appNames.Contains("chrome"))
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ByeDPI Tarayıcı Tünelleme Switch Event Handlers
        private async void ChkByeDPIBrowserTunneling_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // app-config.json dosyasına tarayıcıları ekle
                await UpdateProxiFyreConfigAsync(true);
                
                // Sadece config güncellendi, UI değişikliği yapma
                // Switch, sadece ProxiFyreService yüklendikten sonra kaybolacak
                Debug.WriteLine("Tarayıcı tünelleme aktif edildi - config güncellendi");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Tarayıcı tünelleme ayarı yapılırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                if (chkByeDPIBrowserTunneling != null)
                {
                    chkByeDPIBrowserTunneling.IsChecked = false;
                }
            }
        }

        private async void ChkByeDPIBrowserTunneling_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                // app-config.json dosyasından tarayıcıları kaldır
                await UpdateProxiFyreConfigAsync(false);
                
                // Sadece config güncellendi, UI değişikliği yapma
                // Switch, sadece ProxiFyreService yüklendikten sonra kaybolacak
                Debug.WriteLine("Tarayıcı tünelleme pasif edildi - config güncellendi");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Tarayıcı tünelleme ayarı yapılırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                if (chkByeDPIBrowserTunneling != null)
                {
                    chkByeDPIBrowserTunneling.IsChecked = false;
                }
            }
        }

        // ProxiFyre app-config.json dosyasını güncelleyen metot
        private async Task UpdateProxiFyreConfigAsync(bool addBrowsers)
        {
            await Task.Run(() =>
            {
                try
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ProxiFyre app-config.json güncelleniyor...\n");
                    
                    // app-config.json dosyasının yolunu al
                    var currentDir = Directory.GetCurrentDirectory();
                    var configPath = Path.Combine(currentDir, "res", "proxifyre", "app-config.json");
                    
                    if (!File.Exists(configPath))
                    {
                        File.AppendAllText(logPath, $"HATA: app-config.json dosyası bulunamadı: {configPath}\n");
                        throw new FileNotFoundException($"app-config.json dosyası bulunamadı: {configPath}");
                    }
                    
                    // JSON dosyasını oku
                    var jsonContent = File.ReadAllText(configPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<ProxiFyreConfig>(jsonContent);
                    
                    if (config == null)
                    {
                        File.AppendAllText(logPath, "HATA: app-config.json dosyası parse edilemedi.\n");
                        throw new InvalidOperationException("app-config.json dosyası parse edilemedi.");
                    }
                    
                    // Tarayıcı uygulamaları
                    var browserApps = new[] { "browser", "chrome", "firefox", "opera", "operagx", "brave", "vivaldi", "msedge" };
                    
                    if (addBrowsers)
                    {
                        // Tarayıcıları ekle
                        foreach (var proxy in config.proxies)
                        {
                            if (proxy.appNames != null)
                            {
                                foreach (var browser in browserApps)
                                {
                                    if (!proxy.appNames.Contains(browser))
                                    {
                                        proxy.appNames.Add(browser);
                                        File.AppendAllText(logPath, $"Tarayıcı eklendi: {browser}\n");
                                    }
                                }
                            }
                        }
                        File.AppendAllText(logPath, "Tarayıcılar app-config.json dosyasına eklendi.\n");
                    }
                    else
                    {
                        // Tarayıcıları kaldır
                        foreach (var proxy in config.proxies)
                        {
                            if (proxy.appNames != null)
                            {
                                foreach (var browser in browserApps)
                                {
                                    proxy.appNames.RemoveAll(app => app == browser);
                                }
                            }
                        }
                        File.AppendAllText(logPath, "Tarayıcılar app-config.json dosyasından kaldırıldı.\n");
                    }
                    
                    // Dosyayı kaydet
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(config, options);
                    File.WriteAllText(configPath, updatedJson);
                    
                    File.AppendAllText(logPath, "app-config.json dosyası başarıyla güncellendi.\n");
                }
                catch (Exception ex)
                {
                    var logPath = GetLogPath();
                    File.AppendAllText(logPath, $"ProxiFyre config güncelleme hatası: {ex.Message}\n");
                    throw;
                }
            });
        }

        // ProxiFyre config sınıfı
        private class ProxiFyreConfig
        {
            public string logLevel { get; set; } = "";
            public List<ProxiFyreProxy> proxies { get; set; } = new List<ProxiFyreProxy>();
        }

        private class ProxiFyreProxy
        {
            public List<string> appNames { get; set; } = new List<string>();
            public string socks5ProxyEndpoint { get; set; } = "";
            public List<string> supportedProtocols { get; set; } = new List<string>();
        }

        // DNS ve DoH Ayarlarını Geri Al Butonu Event Handler
        private async void BtnResetDNSDoH_Click(object sender, RoutedEventArgs e)
        {
            // Onay mesaj kutusu göster
            var result = System.Windows.MessageBox.Show(
                "DNS ve DoH ayarlarını geri almak istediğinizden emin misiniz?\n\n" +
                "Bu işlem:\n" +
                "• DNS ayarlarını otomatik (DHCP) olarak değiştirecek\n" +
                "• DNS over HTTPS (DoH) özelliğini kapatacak\n" +
                "• İnternet bağlantınızı birkaç saniyeliğine etkileyebilir",
                "DNS ve DoH Ayarlarını Geri Al",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            ShowLoading(true);
            
            try
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları geri alınıyor...\n");

                // Modern DNS ayarlarını geri al
                var resetResult = await ResetModernDNSSettingsAsync();
                
                if (resetResult)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları başarıyla geri alındı.\n");

                    System.Windows.MessageBox.Show(
                        "DNS ve DoH ayarları başarıyla geri alındı.\n\n" +
                        "• DNS ayarları otomatik olarak ayarlandı\n" +
                        "• DNS over HTTPS (DoH) kapatıldı",
                        "Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları geri alınırken hata oluştu.\n");

                    System.Windows.MessageBox.Show(
                        "DNS ve DoH ayarları geri alınırken hata oluştu.\n\n" +
                        "Lütfen log dosyasını kontrol edin.",
                        "Hata",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DNS ve DoH ayarları geri alma hatası: {ex.Message}\n");

                System.Windows.MessageBox.Show(
                    $"DNS ve DoH ayarları geri alınırken hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        // SplitWire-Turkey'i Kaldır Butonu Event Handler
        private async void BtnUninstallSplitWire_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "SplitWire-Turkey'i sisteminizden kaldırmak istediğinizden emin misiniz?\n\n" +
                "Bu işlem çalışmakta olan tüm aşım yöntemlerini kapatıp kaldıracak.",
                "SplitWire-Turkey Kaldırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            ShowLoading(true);

            try
            {
                var logPath = GetUninstallLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SplitWire-Turkey kaldırma işlemi başlatılıyor...\n");

                // 1. DNS ve DoH değişikliklerini geri al
                File.AppendAllText(logPath, "1. DNS ve DoH ayarları geri alınıyor...\n");
                var dnsResult = await ResetModernDNSSettingsAsync();
                if (dnsResult)
                {
                    File.AppendAllText(logPath, "1. DNS ve DoH geri alma başarılı.\n");
                }
                else
                {
                    File.AppendAllText(logPath, "1. DNS ve DoH geri alma başarısız, eski yöntem deneniyor...\n");
                    // Eski yöntem olarak yedek
                    var dnsResultOld = ExecuteCommand("netsh", "interface ip set dns \"Ethernet\" dhcp");
                    var dnsResult2Old = ExecuteCommand("netsh", "interface ip set dns \"Wi-Fi\" dhcp");
                    var dohResultOld = ExecuteCommand("netsh", "dns add global dot=off");
                    File.AppendAllText(logPath, "1. Eski yöntemle DNS ve DoH geri alma tamamlandı.\n");
                }

                // 2. Tüm hizmetleri durdur ve kaldır
                File.AppendAllText(logPath, "2. Tüm hizmetler kaldırılıyor...\n");
                var services = new[] { 
                    "GoodbyeDPI", 
                    "zapret", 
                    "byedpi", 
                    "winws1", 
                    "winws2", 
                    "wiresock-client-service", 
                    "ProxiFyreService", 
                    "WinDivert" 
                };
                
                foreach (var service in services)
                {
                    try
                    {
                        if (IsServiceInstalled(service))
                        {
                            ExecuteCommand("sc", $"stop {service}");
                            await Task.Delay(1000);
                            ExecuteCommand("sc", $"delete {service}");
                            await Task.Delay(1000);
                            File.AppendAllText(logPath, $"2. {service} hizmeti kaldırıldı.\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"2. {service} hizmeti kaldırılırken hata: {ex.Message}\n");
                    }
                }

                // 3. WireSock'u kaldır
                File.AppendAllText(logPath, "3. WireSock kaldırılıyor...\n");
                await RemoveWireSockAsync(logPath);
                File.AppendAllText(logPath, "3. WireSock kaldırma tamamlandı.\n");

                // 4. WireSock'un her iki sürümünü de sessiz kaldır
                File.AppendAllText(logPath, "4. WireSock'un her iki sürümü sessiz kaldırılıyor...\n");
                
                // 4.1. WireSock 2.4.16.1 sürümünü sessiz kaldır (Standart Kurulum)
                try
                {
                    var wiresockUninstallPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "wiresock-secure-connect-x64-2.4.16.1.exe");
                    if (File.Exists(wiresockUninstallPath))
                    {
                        File.AppendAllText(logPath, "4.1. WireSock 2.4.16.1 sürümü kaldırılıyor...\n");
                        var uninstallProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = wiresockUninstallPath,
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
                        File.AppendAllText(logPath, $"4.1. WireSock 2.4.16.1 kaldırma tamamlandı. Exit Code: {uninstallProcess.ExitCode}\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "4.1. WireSock 2.4.16.1 kaldırma dosyası bulunamadı.\n");
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"4.1. WireSock 2.4.16.1 kaldırma hatası: {ex.Message}\n");
                    // Hata olsa bile devam et
                }

                // 4.2. WireSock 1.4.7.1 sürümünü sessiz kaldır (Alternatif Kurulum)
                try
                {
                    var wiresockLegacyUninstallPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "wiresock-vpn-client-x64-1.4.7.1.msi");
                    if (File.Exists(wiresockLegacyUninstallPath))
                    {
                        File.AppendAllText(logPath, "4.2. WireSock 1.4.7.1 sürümü kaldırılıyor...\n");
                        var uninstallProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "msiexec",
                                Arguments = $"/x \"{wiresockLegacyUninstallPath}\" /qn",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                Verb = "runas"
                            }
                        };

                        uninstallProcess.Start();
                        await uninstallProcess.WaitForExitAsync();
                        File.AppendAllText(logPath, $"4.2. WireSock 1.4.7.1 kaldırma tamamlandı. Exit Code: {uninstallProcess.ExitCode}\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "4.2. WireSock 1.4.7.1 kaldırma dosyası bulunamadı.\n");
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"4.2. WireSock 1.4.7.1 kaldırma hatası: {ex.Message}\n");
                    // Hata olsa bile devam et
                }

                // 4.3. %localappdata%/SplitWire-Turkey klasörünü sil
                try
                {
                    File.AppendAllText(logPath, "4.3. %localappdata%/SplitWire-Turkey klasörü siliniyor...\n");
                    var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var splitWireTurkeyPath = Path.Combine(localAppDataPath, "SplitWire-Turkey");
                    
                    if (Directory.Exists(splitWireTurkeyPath))
                    {
                        Directory.Delete(splitWireTurkeyPath, true);
                        File.AppendAllText(logPath, "4.3. %localappdata%/SplitWire-Turkey klasörü başarıyla silindi.\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, "4.3. %localappdata%/SplitWire-Turkey klasörü bulunamadı.\n");
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"4.3. %localappdata%/SplitWire-Turkey klasörü silme hatası: {ex.Message}\n");
                    // Hata olsa bile devam et
                }

                // 5. unins000.exe kontrol et
                File.AppendAllText(logPath, "5. Uninstall dosyası kontrol ediliyor...\n");
                var currentDir = Directory.GetCurrentDirectory();
                var uninstallPath = Path.Combine(currentDir, "unins000.exe");

                if (File.Exists(uninstallPath))
                {
                    File.AppendAllText(logPath, "5. unins000.exe bulundu, çalıştırılıyor...\n");
                    
                    // Uninstall uygulamasını çalıştır
                    var psi = new ProcessStartInfo
                    {
                        FileName = uninstallPath,
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                    
                    // Uygulamayı kapat
                    System.Windows.Application.Current.Shutdown();
                }
                else
                {
                    File.AppendAllText(logPath, "5. unins000.exe bulunamadı.\n");
                    ShowLoading(false);

                    System.Windows.MessageBox.Show(
                        "Uninstall dosyası bulunamadı.\n\n" +
                        "ZIP olarak kullanım sağlıyorsanız programı kapatıp tüm dosyaları silebilirsiniz.",
                        "Bilgilendirme",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // Hizmet durumlarını güncelle
                    await ForceRefreshAllServicesAsync();
                }
            }
            catch (Exception ex)
            {
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SplitWire-Turkey kaldırma hatası: {ex.Message}\n");

                System.Windows.MessageBox.Show(
                    $"SplitWire-Turkey kaldırma sırasında hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        // WireSock kaldırma yardımcı metodu
        private async Task RemoveWireSockAsync(string logPath)
        {
            try
            {
                // WireSock hizmetini durdur ve kaldır
                if (IsServiceInstalled("wiresock-client-service"))
                {
                    ExecuteCommand("sc", "stop wiresock-client-service");
                    await Task.Delay(2000);
                    ExecuteCommand("sc", "delete wiresock-client-service");
                    await Task.Delay(1000);
                    File.AppendAllText(logPath, "wiresock-client-service kaldırıldı.\n");
                }

                // WireSock kısayolunu sil
                RemoveWireSockShortcut(logPath);

                // WireSock konfigürasyon dosyalarını sil
                var currentDir = Directory.GetCurrentDirectory();
                var configPath = Path.Combine(currentDir, "wiresock.conf");
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                    File.AppendAllText(logPath, "wiresock.conf silindi.\n");
                }

                File.AppendAllText(logPath, "WireSock kaldırma tamamlandı.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"WireSock kaldırma hatası: {ex.Message}\n");
            }
        }

        // Uninstall log dosyası yolu
        private string GetUninstallLogPath()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exePath);
            var logsDirectory = Path.Combine(exeDirectory, "logs");
            
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, "uninstall.log");
        }
    }
} 