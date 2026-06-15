using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
            using (Mutex mutex = new Mutex(true, "TetraClip_SingleInstance_{8F3A1C2E}", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("TetraClip is already running. Look for its icon in the system tray.",
                        "TetraClip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
        }
    }

    public class MainForm : Form
    {
        // ---- Win32 interop ----
        const int WM_HOTKEY = 0x0312;
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const uint MOD_ALT = 0x0001;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // ---- State ----
        readonly TextBox[] _boxes = new TextBox[4];
        readonly Panel[] _frames = new Panel[4];
        int _active = 0;
        bool _sync = true;
        string _lastSetByApp = null;
        bool _dirty = false;
        bool _reallyExit = false;

        NotifyIcon _tray;
        System.Windows.Forms.Timer _saveTimer;
        Icon _appIcon;
        ToolStripMenuItem _startupItem;

        static readonly Color AccentColor = Color.FromArgb(0, 120, 215);
        static readonly Color FrameIdle = Color.FromArgb(205, 205, 205);

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppRegName = "TetraClip";

        string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TetraClip"); } }
        string DataFile { get { return Path.Combine(DataDir, "slots.dat"); } }

        public MainForm()
        {
            _appIcon = BuildIcon();
            this.Icon = _appIcon;
            this.Text = "TetraClip";
            this.ClientSize = new Size(280, 360);
            this.MinimumSize = new Size(240, 200);
            this.StartPosition = FormStartPosition.Manual;
            PositionBottomRight();

            BuildUI();
            LoadData();
            SetupTray();
            UpdateActiveVisual();

            this.Load += OnLoaded;
            this.FormClosing += OnFormClosing;

            _saveTimer = new System.Windows.Forms.Timer();
            _saveTimer.Interval = 1500;
            _saveTimer.Tick += delegate { if (_dirty) { SaveData(); _dirty = false; } };
            _saveTimer.Start();
        }

        void OnLoaded(object sender, EventArgs e)
        {
            PositionBottomRight();
            RegisterHotkeys();
            AddClipboardFormatListener(this.Handle);
        }

        // ---- UI: four stacked paste boxes, nothing else ----
        void BuildUI()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            root.Padding = new Padding(2);
            root.BackColor = Color.FromArgb(245, 245, 245);

            for (int i = 0; i < 4; i++)
                root.Controls.Add(BuildSlot(i), 0, i);

            this.Controls.Add(root);
        }

        Control BuildSlot(int index)
        {
            int captured = index;

            // Coloured frame = the only indicator of which slot is active
            Panel frame = new Panel();
            frame.Dock = DockStyle.Fill;
            frame.Margin = new Padding(6, 4, 6, 4);
            frame.Padding = new Padding(3);
            frame.BackColor = FrameIdle;

            TextBox box = new TextBox();
            box.Multiline = true;
            box.Dock = DockStyle.Fill;
            box.ScrollBars = ScrollBars.None;
            box.WordWrap = true;
            box.AcceptsTab = true;
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Consolas", 9f);
            box.HideSelection = false;
            box.MouseDown += delegate { SetActiveSlot(captured, true); };  // click -> activate + push to clipboard
            box.Enter += delegate { SetActiveSlot(captured, false); };     // focus only -> activate, don't touch clipboard
            box.TextChanged += delegate { _dirty = true; };
            _boxes[index] = box;

            frame.Controls.Add(box);
            _frames[index] = frame;
            return frame;
        }

        // ---- Hotkeys / clipboard messages ----
        void RegisterHotkeys()
        {
            bool allOk = true;
            for (int i = 0; i < 4; i++)
            {
                bool ok = RegisterHotKey(this.Handle, i + 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)(0x31 + i));
                if (!ok) allOk = false;
            }
            if (!allOk)
                ShowBalloon("Some Ctrl+Alt+1…4 hotkeys are already used by another app.");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id >= 1 && id <= 4) SetActiveSlot(id - 1, true);
            }
            else if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate();
            }
            base.WndProc(ref m);
        }

        // Up / Down arrow keys cycle through the four slots while the window is focused
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Down || keyData == Keys.Up)
            {
                int next = (keyData == Keys.Down) ? (_active + 1) % 4 : (_active + 3) % 4;
                SetActiveSlot(next, true);
                _boxes[next].Focus();
                _boxes[next].SelectionStart = _boxes[next].TextLength;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void SetActiveSlot(int index, bool pushToClipboard)
        {
            if (index < 0 || index > 3) return;
            _active = index;
            UpdateActiveVisual();
            if (pushToClipboard)
            {
                PushSlotToClipboard(index);
                if (!this.Visible) ShowBalloon("Slot " + (index + 1) + " is now the clipboard.");
            }
            _dirty = true;
        }

        void PushSlotToClipboard(int index)
        {
            string text = _boxes[index].Text;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    _lastSetByApp = text ?? "";
                    if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                    else Clipboard.SetText(text);
                    return;
                }
                catch { Thread.Sleep(40); }
            }
        }

        void OnClipboardUpdate()
        {
            if (!_sync) return;
            string cur = null;
            try { if (Clipboard.ContainsText()) cur = Clipboard.GetText(); }
            catch { return; }
            if (cur == null) return;
            if (cur == _lastSetByApp) return; // change we caused ourselves
            _boxes[_active].Text = cur;
            _lastSetByApp = cur;
            _dirty = true;
        }

        void UpdateActiveVisual()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_frames[i] == null) continue;
                _frames[i].BackColor = (i == _active) ? AccentColor : FrameIdle;
            }
        }

        // ---- Tray + window ----
        void SetupTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "TetraClip — Multi-Clipboard";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Show / Hide window", null, delegate { ToggleWindow(); }));
            menu.Items.Add(new ToolStripSeparator());
            _startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            _startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { _reallyExit = true; this.Close(); }));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ToggleWindow(); };
        }

        void ToggleWindow()
        {
            if (this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                this.Hide();
            }
            else
            {
                this.WindowState = FormWindowState.Normal;
                this.Show();
                PositionBottomRight();
                this.Activate();
                this.BringToFront();
            }
        }

        // Place the window near the bottom-right corner of the screen
        void PositionBottomRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - this.Width - 24;
            int y = wa.Bottom - this.Height - 48;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            this.Location = new Point(x, y);
        }

        void ShowBalloon(string text)
        {
            try
            {
                _tray.BalloonTipTitle = "TetraClip";
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(1500);
            }
            catch { }
        }

        // ---- Run at startup ----
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

        // ---- Close / persistence ----
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }
            SaveData();
            for (int i = 0; i < 4; i++) UnregisterHotKey(this.Handle, i + 1);
            RemoveClipboardFormatListener(this.Handle);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        void SaveData()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("active=" + _active);
                sb.AppendLine("sync=" + (_sync ? "1" : "0"));
                for (int i = 0; i < 4; i++)
                {
                    string raw = _boxes[i].Text ?? "";
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    sb.AppendLine("slot" + i + "=" + b64);
                }
                File.WriteAllText(DataFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void LoadData()
        {
            try
            {
                if (!File.Exists(DataFile)) return;
                string[] lines = File.ReadAllLines(DataFile);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq);
                    string val = line.Substring(eq + 1);
                    if (key == "active") int.TryParse(val, out _active);
                    else if (key == "sync") _sync = (val == "1");
                    else if (key.StartsWith("slot"))
                    {
                        int idx;
                        if (int.TryParse(key.Substring(4), out idx) && idx >= 0 && idx < 4)
                        {
                            try { _boxes[idx].Text = Encoding.UTF8.GetString(Convert.FromBase64String(val)); }
                            catch { }
                        }
                    }
                }
                if (_active < 0 || _active > 3) _active = 0;
            }
            catch { }
        }

        // ---- Icon drawn at runtime (no external file needed) ----
        Icon BuildIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color[] cols = {
                    Color.FromArgb(0, 120, 215),
                    Color.FromArgb(16, 137, 62),
                    Color.FromArgb(216, 99, 22),
                    Color.FromArgb(136, 23, 152)
                };
                int[,] pos = { { 3, 3 }, { 17, 3 }, { 3, 17 }, { 17, 17 } };
                for (int i = 0; i < 4; i++)
                {
                    using (SolidBrush b = new SolidBrush(cols[i]))
                        g.FillRectangle(b, pos[i, 0], pos[i, 1], 12, 12);
                }
            }
            IntPtr hicon = bmp.GetHicon();
            Icon ic = Icon.FromHandle(hicon);
            bmp.Dispose();
            return ic;
        }
    }
}
