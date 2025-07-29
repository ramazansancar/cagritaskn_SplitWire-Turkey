using System;
using System.Windows;
using System.Windows.Threading;

namespace SplitWireTurkey
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
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
    }
} 