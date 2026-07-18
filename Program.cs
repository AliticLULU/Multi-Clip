using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TetraClip
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "TetraClip_SingleInstance_{8F3A1C2E}", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running — just open the page again
                    OpenMemoPage();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }

        static string HtmlPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memo.html"); }
        }

        public static void OpenMemoPage()
        {
            try
            {
                Process.Start(HtmlPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot open memo.html.\n\n" + ex.Message,
                    "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class TrayApp : ApplicationContext
    {
        NotifyIcon _tray;
        Icon _appIcon;
        ToolStripMenuItem _startupItem;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppRegName = "TetraClip";

        public TrayApp()
        {
            _appIcon = BuildIcon();

            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "MemoClip — Key-Value Memos";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem showItem = new ToolStripMenuItem("📝 Open Memos", null, delegate { Program.OpenMemoPage(); });
            showItem.Font = new Font(showItem.Font, FontStyle.Bold);
            menu.Items.Add(showItem);

            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            _startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(_startupItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Exit(); }));

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { Program.OpenMemoPage(); };

            // Open the page on startup
            Program.OpenMemoPage();
        }

        void Exit()
        {
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppRegName) != null;
            }
            catch { return false; }
        }

        void ToggleStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (IsStartupEnabled())
                    {
                        k.DeleteValue(AppRegName, false);
                        _startupItem.Checked = false;
                    }
                    else
                    {
                        k.SetValue(AppRegName, "\"" + Application.ExecutablePath + "\"");
                        _startupItem.Checked = true;
                    }
                }
            }
            catch { }
        }

        Icon BuildIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(79, 110, 247)))
                    g.FillRectangle(bg, 5, 2, 22, 26);
                Point[] fold = { new Point(27, 2), new Point(27, 10), new Point(19, 2) };
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(59, 90, 220)))
                    g.FillPolygon(fb, fold);
                using (Pen lp = new Pen(Color.FromArgb(255, 255, 255, 90)))
                    for (int y = 8; y <= 22; y += 5)
                        g.DrawLine(lp, 9, y, 23, y);
            }
            IntPtr hicon = bmp.GetHicon();
            Icon ic = Icon.FromHandle(hicon);
            bmp.Dispose();
            return ic;
        }
    }
}
