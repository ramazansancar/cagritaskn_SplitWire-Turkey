using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;
using SplitWireTurkey.Services;

namespace SplitWireTurkey
{
    public partial class MainWindow : Window
    {
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
                
                System.Windows.MessageBox.Show("Tüm hizmetler başarıyla kaldırıldı. Kuruluma devam ediliyor...", 
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
            
            try
            {
                var success = await _wireGuardService.CreateProfileAsync();
                if (success)
                {
                    var configPath = _wireGuardService.GetConfigPath();
                    await _wireSockService.InstallServiceAsync(configPath);
                }
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task PerformCustomSetup()
        {
            ShowLoading(true);
            
            try
            {
                var extraFolders = _folders.ToArray();
                var success = await _wireGuardService.CreateProfileAsync(extraFolders);
                if (success)
                {
                    var configPath = _wireGuardService.GetConfigPath();
                    await _wireSockService.InstallServiceAsync(configPath);
                }
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task GenerateConfigOnly()
        {
            ShowLoading(true);
            
            try
            {
                var extraFolders = _folders.ToArray();
                var success = await _wireGuardService.CreateProfileAsync(extraFolders);
                
                if (success)
                {
                    var configPath = _wireGuardService.GetConfigPath();
                    System.Windows.MessageBox.Show($"Profil dosyası oluşturuldu:\n{configPath}", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task PerformAlternativeSetup()
        {
            ShowLoading(true);
            try
            {
                // Önce mevcut WireSock hizmetini durdur ve kaldır
                var uninstallSuccess = await _wireSockService.RemoveServiceAsync();
                if (!uninstallSuccess)
                {
                    System.Windows.MessageBox.Show("Mevcut WireSock hizmeti kaldırılamadı. Alternatif kurulum başlatılamadı.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Mevcut WireSock kurulumunu kaldır - son sürüm setup dosyasını indir ve /uninstall /S ile kaldır
                var tempDir = Path.Combine(Path.GetTempPath(), "SplitWireTurkey");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                var currentSetupPath = Path.Combine(tempDir, "wiresock-current.exe");
                var currentDownloadUrl = "https://wiresock.net/_api/download-release.php?product=wiresock-secure-connect&platform=windows_x64&version=latest";

                try
                {
                    // Mevcut sürümü indir
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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mevcut sürüm kaldırma hatası: {ex.Message}");
                    // Hata olsa bile devam et
                }

                // 1.4.7.1 sürümünü indir ve kur
                var setupPath = Path.Combine(tempDir, "wiresock-legacy.msi");
                var resSetupPath = Path.Combine(Environment.CurrentDirectory, "res", "wiresock-vpn-client-x64-1.4.7.1.msi");

                bool downloadSuccess = false;
                
                // Önce /res klasöründen dosyayı kopyalamayı dene
                if (File.Exists(resSetupPath))
                {
                    File.Copy(resSetupPath, setupPath, true);
                    downloadSuccess = true;
                }
                else
                {
                    // /res klasöründe yoksa GitHub'dan indirmeyi dene
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GitHub'dan indirme hatası: {ex.Message}");
                        
                        // GitHub'dan da indirilemezse yerel dosyayı dene
                        var localSetupPath = Path.Combine(Environment.CurrentDirectory, "wiresock-vpn-client-x64-1.4.7.1.msi");
                        if (File.Exists(localSetupPath))
                        {
                            File.Copy(localSetupPath, setupPath, true);
                            downloadSuccess = true;
                            System.Windows.MessageBox.Show("GitHub'dan indirilemedi, yerel dosya kullanılıyor.", 
                                "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("1.4.7.1 sürümü bulunamadı.\nLütfen wiresock-vpn-client-x64-1.4.7.1.msi dosyasını /res klasörüne kopyalayın.", 
                                "Dosya Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }

                if (!downloadSuccess)
                {
                    System.Windows.MessageBox.Show("1.4.7.1 sürümü indirilemedi.", 
                        "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // En uygun kurulum yolunu belirle
                var installPath = GetBestInstallPath();

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
                
                foreach (var msiArgs in msiArgsList)
                {
                    try
                    {
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
                            System.Windows.MessageBox.Show($"WireSock 1.4.7.1 sürümü sessiz kurulumu tamamlandı.\nKuruluma devam ediliyor ...", 
                                "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                            msiInstallSuccess = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Bu parametre çalışmadı, diğerini dene
                        continue;
                    }
                }

                // MSI kurulum başarısız olursa normal kurulumu dene
                if (!msiInstallSuccess)
                {
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
                    
                    System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kurulumu tamamlandı. Kuruluma devam ediliyor ...", 
                        "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                
                UpdateWireSockStatus();
                
                // Kısa bir bekleme süresi ekle
                await Task.Delay(2000);
                
                // Hizmet kurulumu yap
                var success = await _wireGuardService.CreateProfileAsync();
                if (success)
                {
                    var configPath = _wireGuardService.GetConfigPath();
                    var serviceResult = await _wireSockService.InstallServiceAsync(configPath);
                    
                    if (serviceResult)
                    {
                        System.Windows.MessageBox.Show("Alternatif kurulum tamamlandı.", 
                            "Alternatif Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kuruldu ancak hizmet başlatılamadı. Manuel olarak başlatmayı deneyin.", 
                            "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("WireSock 1.4.7.1 sürümü kuruldu ancak profil oluşturulamadı.", 
                        "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Alternatif kurulum sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
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
    }
} 