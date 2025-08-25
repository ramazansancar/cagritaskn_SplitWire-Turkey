using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Diagnostics;

namespace SplitWireTurkey
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;
        private const string MutexName = "SplitWireTurkeySingleInstanceMutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Check if another instance is already running
            if (!CheckSingleInstance())
            {
                MessageBox.Show(
                    "SplitWire-Turkey zaten çalışıyor. Pencereyi göremiyorsanız Görev Yöneticisi kullanarak SplitWire-Turkey.exe'yi sonlandırın.",
                    "Uygulama Zaten Çalışıyor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
            
            // Set up global exception handling
            Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "Bu uygulama yönetici izinleri gerektirir. Lütfen yönetici olarak çalıştırın.",
                    "Yönetici İzinleri Gerekli",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n{e.Exception.Message}",
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Kritik bir hata oluştu:\n{e.ExceptionObject}",
                "Kritik Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private bool CheckSingleInstance()
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out bool createdNew);
                return createdNew;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tek instance kontrolü sırasında hata: {ex.Message}");
                return false;
            }
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Mutex'i temizle
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
            
            base.OnExit(e);
        }
    }
} 