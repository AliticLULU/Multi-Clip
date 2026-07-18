using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MemoClip
{
    public partial class MainWindow : Window
    {
        // ---- Data ----
        ObservableCollection<MemoEntry> _memos = new ObservableCollection<MemoEntry>();
        ObservableCollection<MemoEntry> _filteredMemos = new ObservableCollection<MemoEntry>();

        // ---- Tray ----
        NotifyIcon _tray;
        System.Drawing.Icon _appIcon;
        bool _reallyExit = false;

        // ---- Paths ----
        string DataDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); } }
        string MemoFile { get { return Path.Combine(DataDir, "memos.dat"); } }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppRegName = "MemoClip";

        public MainWindow()
        {
            InitializeComponent();
            PositionBottomRight();
            SetupTray();

            // Set window icon from the same GDI icon
            System.Drawing.Icon gdiIcon = BuildIcon();
            this.Icon = Imaging.CreateBitmapSourceFromHIcon(
                gdiIcon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            LoadMemos();
            ApplyFilter();

            this.KeyDown += OnWindowKeyDown;
            this.Closing += OnWindowClosing;
            this.Loaded += (s, e) => SearchBox.Focus();
        }

        // ---- Window events ----
        void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
            {
                e.Handled = true;
                OpenAddDialog();
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SearchBox.Clear();
                SearchBox.Focus();
            }
            else if (e.Key == Key.Delete && !SearchBox.IsFocused)
            {
                // Delete is handled per-card via buttons
            }
        }

        void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (!_reallyExit)
            {
                e.Cancel = true;
                this.Hide();
                SaveMemos(); // save when hiding to tray
                return;
            }
            SaveMemos();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        // ---- Title bar ----
        void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click title bar toggles maximize
                if (this.WindowState == WindowState.Maximized)
                    this.WindowState = WindowState.Normal;
                else
                    this.WindowState = WindowState.Maximized;
            }
            else
            {
                this.DragMove();
            }
        }

        void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        // ---- Search / Filter ----
        void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        void ApplyFilter()
        {
            string q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
            _filteredMemos.Clear();
            foreach (var m in _memos)
            {
                if (string.IsNullOrEmpty(q) ||
                    (m.Key ?? "").ToLowerInvariant().Contains(q) ||
                    (m.Value ?? "").ToLowerInvariant().Contains(q))
                {
                    _filteredMemos.Add(m);
                }
            }
            MemoListControl.ItemsSource = _filteredMemos;
            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            int total = _memos.Count;
            int showing = _filteredMemos.Count;

            if (total == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                MemoListControl.Visibility = Visibility.Collapsed;
                CountLabel.Text = "· 0 memos";
                StatusLabel.Text = "Ready — press Ctrl+N to add a memo";
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                MemoListControl.Visibility = Visibility.Visible;

                string q = (SearchBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(q))
                {
                    CountLabel.Text = "· " + total + " memo" + (total != 1 ? "s" : "");
                    StatusLabel.Text = total + " memo" + (total != 1 ? "s" : "");
                }
                else
                {
                    CountLabel.Text = "· " + showing + " of " + total;
                    StatusLabel.Text = "Showing " + showing + " of " + total + " — \"" + q + "\"";
                }
            }
        }

        // ---- CRUD ----
        void AddMemo_Click(object sender, RoutedEventArgs e)
        {
            OpenAddDialog();
        }

        void EditMemo_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            MemoEntry entry = btn.Tag as MemoEntry;
            if (entry == null) return;
            OpenEditDialog(entry);
        }

        void DeleteMemo_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            MemoEntry entry = btn.Tag as MemoEntry;
            if (entry == null) return;

            string preview = (entry.Key ?? "").Length > 40
                ? (entry.Key ?? "").Substring(0, 40) + "..."
                : (entry.Key ?? "");

            MessageBoxResult dr = MessageBox.Show(
                "Delete memo \"" + preview + "\"?\n\nThis cannot be undone.",
                "MemoClip",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (dr == MessageBoxResult.Yes)
            {
                _memos.Remove(entry);
                SaveMemos();
                ApplyFilter();
            }
        }

        void OpenAddDialog()
        {
            MemoDialog dlg = new MemoDialog("Add Memo", "", "");
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (dlg.ShowDialog() == true)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show("Please enter a key name.", "MemoClip",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _memos.Add(new MemoEntry { Key = dlg.MemoKey, Value = dlg.MemoValue });
                SaveMemos();
                ApplyFilter();
            }
        }

        void OpenEditDialog(MemoEntry entry)
        {
            MemoDialog dlg = new MemoDialog("Edit Memo", entry.Key, entry.Value);
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (dlg.ShowDialog() == true)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show("Please enter a key name.", "MemoClip",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                entry.Key = dlg.MemoKey;
                entry.Value = dlg.MemoValue;
                SaveMemos();
                ApplyFilter();
            }
        }

        // ---- Persistence ----
        void SaveMemos()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                StringBuilder sb = new StringBuilder();
                foreach (var m in _memos)
                {
                    string keyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Key ?? ""));
                    string valB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Value ?? ""));
                    sb.AppendLine(keyB64 + "=" + valB64);
                }
                File.WriteAllText(MemoFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Save error: " + ex.Message);
            }
        }

        void LoadMemos()
        {
            try
            {
                if (!File.Exists(MemoFile)) return;
                _memos.Clear();
                string[] lines = File.ReadAllLines(MemoFile);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    try
                    {
                        string k = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(0, eq)));
                        string v = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(eq + 1)));
                        _memos.Add(new MemoEntry { Key = k, Value = v });
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ---- Tray ----
        void SetupTray()
        {
            _appIcon = BuildIcon();

            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "MemoClip — Key-Value Memos";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem showItem = new ToolStripMenuItem("📝 Open MemoClip", null,
                delegate { ShowWindow(); });
            showItem.Font = new System.Drawing.Font(showItem.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(showItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startupItem = new ToolStripMenuItem("Start with Windows", null,
                delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(startupItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate
            {
                _reallyExit = true;
                this.Close();
            }));

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowWindow(); };
        }

        void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.BringIntoView();
            SearchBox.Focus();
        }

        void PositionBottomRight()
        {
            var wa = System.Windows.SystemParameters.WorkArea;
            this.Left = wa.Right - this.Width - 30;
            this.Top = wa.Bottom - this.Height - 50;
            if (this.Left < wa.Left) this.Left = wa.Left;
            if (this.Top < wa.Top) this.Top = wa.Top;
        }

        // ---- Startup ----
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
                        k.DeleteValue(AppRegName, false);
                    else
                        k.SetValue(AppRegName, System.Windows.Forms.Application.ExecutablePath);
                }
            }
            catch { }
        }

        // ---- Icon ----
        System.Drawing.Icon BuildIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using (SolidBrush bg = new SolidBrush(System.Drawing.Color.FromArgb(79, 110, 247)))
                    g.FillRectangle(bg, 5, 2, 22, 26);
                System.Drawing.Point[] fold = {
                    new System.Drawing.Point(27, 2),
                    new System.Drawing.Point(27, 10),
                    new System.Drawing.Point(19, 2)
                };
                using (SolidBrush fb = new SolidBrush(System.Drawing.Color.FromArgb(59, 90, 220)))
                    g.FillPolygon(fb, fold);
                using (Pen lp = new Pen(System.Drawing.Color.FromArgb(255, 255, 255, 90)))
                    for (int y = 8; y <= 22; y += 5)
                        g.DrawLine(lp, 9, y, 23, y);
            }
            IntPtr hicon = bmp.GetHicon();
            System.Drawing.Icon ic = System.Drawing.Icon.FromHandle(hicon);
            bmp.Dispose();
            return ic;
        }
    }

    // ---- Data Model ----
    public class MemoEntry : INotifyPropertyChanged
    {
        string _key = "";
        string _value = "";
        public string Key { get { return _key; } set { _key = value; OnChanged("Key"); } }
        public string Value { get { return _value; } set { _value = value; OnChanged("Value"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
