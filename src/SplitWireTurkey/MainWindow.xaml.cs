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

            if (!_wireSockService.IsWireSockInstalled())
            {
                var downloadResult = System.Windows.MessageBox.Show("WireSock yüklü değil. WireSock kurulum dosyasını indirip çalıştırmak ister misiniz?", 
                    "WireSock Yüklü Değil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (downloadResult == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallWireSock();
                }
                return;
            }

            await PerformFastSetup();
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

            if (!_wireSockService.IsWireSockInstalled())
            {
                var downloadResult = System.Windows.MessageBox.Show("WireSock yüklü değil. WireSock kurulum dosyasını indirip çalıştırmak ister misiniz?", 
                    "WireSock Yüklü Değil", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (downloadResult == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallWireSock();
                }
                return;
            }

            await PerformCustomSetup();
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
            var result = System.Windows.MessageBox.Show("WireSock hizmeti sistemden kaldırılacak. Devam etmek istiyor musunuz?", 
                "Emin misiniz?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            try
            {
                ShowLoading(true);
                var success = await _wireSockService.RemoveServiceAsync();
                
                if (success)
                {
                    System.Windows.MessageBox.Show("WireSock hizmeti başarıyla kaldırıldı.", 
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hizmet kaldırılırken hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
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

                // Kurulum dosyasını çalıştır
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = setupPath,
                        UseShellExecute = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

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

                System.Windows.MessageBox.Show("WireSock kurulumu tamamlandı. Şimdi hızlı kurulumu başlatabilirsiniz.", 
                    "Kurulum Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateWireSockStatus();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"WireSock kurulumu sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
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