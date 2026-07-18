using System;
using System.Windows;
using System.Threading;

namespace MemoClip
{
    public partial class App : Application
    {
        static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(true, "MemoClip_SingleInstance_{D4F2B8A1}", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("MemoClip is already running. Look for its icon in the system tray.",
                    "MemoClip", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
                _mutex.Close();
            base.OnExit(e);
        }
    }
}
