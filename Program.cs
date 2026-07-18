using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TetraClip
{
    // ---- Data class for a single memo entry ----
    public class MemoEntry
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    // ---- Dialog for adding / editing a memo ----
    public class MemoEditForm : Form
    {
        TextBox _keyBox;
        TextBox _valueBox;

        public string MemoKey
        {
            get { return (_keyBox.Text ?? "").Trim(); }
            set { _keyBox.Text = value ?? ""; }
        }

        public string MemoValue
        {
            get { return _valueBox.Text ?? ""; }
            set { _valueBox.Text = value ?? ""; }
        }

        public MemoEditForm(string title, string initialKey, string initialValue)
        {
            this.Text = title;
            this.ClientSize = new Size(440, 250);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.Padding = new Padding(12);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            // Key label + textbox
            Label keyLabel = new Label();
            keyLabel.Text = "Key:";
            keyLabel.TextAlign = ContentAlignment.MiddleRight;
            keyLabel.Dock = DockStyle.Fill;
            root.Controls.Add(keyLabel, 0, 0);

            _keyBox = new TextBox();
            _keyBox.Dock = DockStyle.Fill;
            _keyBox.Font = new Font("Consolas", 9.5f);
            _keyBox.Text = initialKey ?? "";
            root.Controls.Add(_keyBox, 1, 0);

            // Value label + textbox
            Label valLabel = new Label();
            valLabel.Text = "Value:";
            valLabel.TextAlign = ContentAlignment.TopRight;
            valLabel.Padding = new Padding(0, 6, 0, 0);
            valLabel.Dock = DockStyle.Fill;
            root.Controls.Add(valLabel, 0, 1);

            _valueBox = new TextBox();
            _valueBox.Multiline = true;
            _valueBox.Dock = DockStyle.Fill;
            _valueBox.Font = new Font("Consolas", 9.5f);
            _valueBox.ScrollBars = ScrollBars.Vertical;
            _valueBox.AcceptsTab = true;
            _valueBox.Text = initialValue ?? "";
            root.Controls.Add(_valueBox, 1, 1);

            // Buttons
            Panel btnPanel = new Panel();
            btnPanel.Dock = DockStyle.Fill;
            root.Controls.Add(btnPanel, 1, 2);

            Button cancelBtn = new Button();
            cancelBtn.Text = "Cancel";
            cancelBtn.DialogResult = DialogResult.Cancel;
            cancelBtn.Location = new Point(btnPanel.Width - 90, 4);
            cancelBtn.Anchor = AnchorStyles.Right;
            btnPanel.Controls.Add(cancelBtn);

            Button okBtn = new Button();
            okBtn.Text = "OK";
            okBtn.DialogResult = DialogResult.OK;
            okBtn.Location = new Point(btnPanel.Width - 170, 4);
            okBtn.Anchor = AnchorStyles.Right;
            btnPanel.Controls.Add(okBtn);

            this.AcceptButton = okBtn;
            this.CancelButton = cancelBtn;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (string.IsNullOrEmpty(_keyBox.Text))
                _keyBox.Focus();
            else
                _valueBox.Focus();
        }
    }

    // ---- Application entry ----
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

        // ---- Clipboard state ----
        readonly TextBox[] _boxes = new TextBox[4];
        readonly Panel[] _frames = new Panel[4];
        int _active = 0;
        bool _sync = true;
        string _lastSetByApp = null;
        bool _dirty = false;
        bool _reallyExit = false;

        // ---- Memo state ----
        ListView _memoList;
        TextBox _searchBox;
        Button _addBtn;
        Button _editBtn;
        Button _delBtn;
        List<MemoEntry> _memos = new List<MemoEntry>();
        bool _memoDirty = false;

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
        string MemoFile { get { return Path.Combine(DataDir, "memos.dat"); } }

        public MainForm()
        {
            _appIcon = BuildIcon();
            this.Icon = _appIcon;
            this.Text = "TetraClip";
            this.ClientSize = new Size(420, 440);
            this.MinimumSize = new Size(320, 280);
            this.StartPosition = FormStartPosition.Manual;
            PositionBottomRight();

            BuildUI();
            LoadData();
            LoadMemos();
            SetupTray();
            UpdateActiveVisual();

            this.Load += OnLoaded;
            this.FormClosing += OnFormClosing;

            _saveTimer = new System.Windows.Forms.Timer();
            _saveTimer.Interval = 1500;
            _saveTimer.Tick += delegate
            {
                if (_dirty) { SaveData(); _dirty = false; }
                if (_memoDirty) { SaveMemos(); _memoDirty = false; }
            };
            _saveTimer.Start();
        }

        void OnLoaded(object sender, EventArgs e)
        {
            PositionBottomRight();
            RegisterHotkeys();
            AddClipboardFormatListener(this.Handle);
        }

        // ---- UI: tabbed layout (Clipboard + Memos) ----
        void BuildUI()
        {
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9f);

            TabPage clipTab = new TabPage("  📋 Clipboard  ");
            clipTab.UseVisualStyleBackColor = true;
            BuildClipboardTab(clipTab);
            tabs.TabPages.Add(clipTab);

            TabPage memoTab = new TabPage("  📝 Memos  ");
            memoTab.UseVisualStyleBackColor = true;
            BuildMemoTab(memoTab);
            tabs.TabPages.Add(memoTab);

            this.Controls.Add(tabs);
        }

        // ---- Clipboard tab: 4 stacked paste boxes ----
        void BuildClipboardTab(TabPage page)
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

            page.Controls.Add(root);
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
            box.MouseDown += delegate { SetActiveSlot(captured, true); };
            box.Enter += delegate { SetActiveSlot(captured, false); };
            box.TextChanged += delegate { _dirty = true; };
            _boxes[index] = box;

            frame.Controls.Add(box);
            _frames[index] = frame;
            return frame;
        }

        // ---- Memo tab: search + key-value list ----
        void BuildMemoTab(TabPage page)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Padding = new Padding(6, 6, 6, 6);

            // Top bar: search box + buttons
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Fill;
            topBar.Margin = new Padding(0, 0, 0, 4);

            _searchBox = new TextBox();
            _searchBox.Font = new Font("Segoe UI", 9.5f);
            _searchBox.Location = new Point(0, 4);
            _searchBox.Size = new Size(180, 24);
            _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _searchBox.TextChanged += OnSearchChanged;

            // Placeholder-style hint
            _searchBox.ForeColor = SystemColors.GrayText;
            _searchBox.Text = "Search memos...";
            _searchBox.Enter += delegate
            {
                if (_searchBox.Text == "Search memos..." && _searchBox.ForeColor == SystemColors.GrayText)
                {
                    _searchBox.Text = "";
                    _searchBox.ForeColor = SystemColors.WindowText;
                }
            };
            _searchBox.Leave += delegate
            {
                if (string.IsNullOrWhiteSpace(_searchBox.Text))
                {
                    _searchBox.Text = "Search memos...";
                    _searchBox.ForeColor = SystemColors.GrayText;
                }
            };

            topBar.Controls.Add(_searchBox);

            int btnX = topBar.Width - 80;
            _addBtn = new Button();
            _addBtn.Text = "+";
            _addBtn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _addBtn.Size = new Size(24, 24);
            _addBtn.Location = new Point(btnX, 4);
            _addBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _addBtn.FlatStyle = FlatStyle.Flat;
            _addBtn.BackColor = Color.FromArgb(0, 120, 215);
            _addBtn.ForeColor = Color.White;
            _addBtn.FlatAppearance.BorderSize = 0;
            _addBtn.Click += delegate { AddMemo(); };
            topBar.Controls.Add(_addBtn);

            _editBtn = new Button();
            _editBtn.Text = "✎";
            _editBtn.Font = new Font("Segoe UI", 8f);
            _editBtn.Size = new Size(24, 24);
            _editBtn.Location = new Point(btnX + 28, 4);
            _editBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _editBtn.FlatStyle = FlatStyle.Flat;
            _editBtn.BackColor = Color.FromArgb(240, 240, 240);
            _editBtn.FlatAppearance.BorderSize = 0;
            _editBtn.Click += delegate { EditMemo(); };
            topBar.Controls.Add(_editBtn);

            _delBtn = new Button();
            _delBtn.Text = "✕";
            _delBtn.Font = new Font("Segoe UI", 8f);
            _delBtn.Size = new Size(24, 24);
            _delBtn.Location = new Point(btnX + 56, 4);
            _delBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _delBtn.FlatStyle = FlatStyle.Flat;
            _delBtn.BackColor = Color.FromArgb(240, 240, 240);
            _delBtn.FlatAppearance.BorderSize = 0;
            _delBtn.Click += delegate { DeleteMemo(); };
            topBar.Controls.Add(_delBtn);

            root.Controls.Add(topBar, 0, 0);

            // Memo list
            _memoList = new ListView();
            _memoList.Dock = DockStyle.Fill;
            _memoList.View = View.Details;
            _memoList.FullRowSelect = true;
            _memoList.GridLines = true;
            _memoList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _memoList.Font = new Font("Consolas", 9f);
            _memoList.Columns.Add("Key", 130, HorizontalAlignment.Left);
            _memoList.Columns.Add("Value", 200, HorizontalAlignment.Left);
            _memoList.DoubleClick += delegate { EditMemo(); };
            _memoList.KeyDown += OnMemoListKeyDown;
            _memoList.SizeChanged += delegate
            {
                if (_memoList.Columns.Count >= 2)
                {
                    int total = _memoList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
                    _memoList.Columns[0].Width = Math.Max(80, total * 35 / 100);
                    _memoList.Columns[1].Width = total - _memoList.Columns[0].Width;
                }
            };

            root.Controls.Add(_memoList, 0, 1);

            page.Controls.Add(root);
        }

        void OnSearchChanged(object sender, EventArgs e)
        {
            if (_searchBox.ForeColor == SystemColors.GrayText) return; // placeholder shown
            RefreshMemoList(_searchBox.Text);
        }

        void OnMemoListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                DeleteMemo();
        }

        // ---- Memo CRUD ----
        void RefreshMemoList(string filter)
        {
            _memoList.BeginUpdate();
            _memoList.Items.Clear();

            string q = (filter ?? "").Trim().ToLowerInvariant();

            foreach (MemoEntry m in _memos)
            {
                if (string.IsNullOrEmpty(q) ||
                    (m.Key != null && m.Key.ToLowerInvariant().Contains(q)) ||
                    (m.Value != null && m.Value.ToLowerInvariant().Contains(q)))
                {
                    ListViewItem item = new ListViewItem(m.Key ?? "");
                    item.SubItems.Add(m.Value ?? "");
                    item.Tag = m;
                    _memoList.Items.Add(item);
                }
            }

            _memoList.EndUpdate();
        }

        void AddMemo()
        {
            MemoEditForm dlg = new MemoEditForm("Add Memo", "", "");
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show(this, "Key cannot be empty.", "TetraClip",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _memos.Add(new MemoEntry { Key = dlg.MemoKey, Value = dlg.MemoValue });
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
            }
            dlg.Dispose();
        }

        void EditMemo()
        {
            if (_memoList.SelectedItems.Count == 0) return;
            ListViewItem item = _memoList.SelectedItems[0];
            MemoEntry orig = item.Tag as MemoEntry;
            if (orig == null) return;

            MemoEditForm dlg = new MemoEditForm("Edit Memo", orig.Key, orig.Value);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show(this, "Key cannot be empty.", "TetraClip",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    dlg.Dispose();
                    return;
                }
                orig.Key = dlg.MemoKey;
                orig.Value = dlg.MemoValue;
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
            }
            dlg.Dispose();
        }

        void DeleteMemo()
        {
            if (_memoList.SelectedItems.Count == 0) return;
            ListViewItem item = _memoList.SelectedItems[0];
            MemoEntry entry = item.Tag as MemoEntry;
            if (entry == null) return;

            string keyPreview = (entry.Key ?? "").Length > 40
                ? (entry.Key ?? "").Substring(0, 40) + "..."
                : (entry.Key ?? "");

            DialogResult dr = MessageBox.Show(this,
                "Delete memo \"" + keyPreview + "\"?",
                "TetraClip",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                _memos.Remove(entry);
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
            }
        }

        string GetSearchText()
        {
            if (_searchBox.ForeColor == SystemColors.GrayText) return "";
            return _searchBox.Text ?? "";
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
            if (cur == _lastSetByApp) return;
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
            _tray.Text = "TetraClip — Multi-Clipboard & Memos";
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
            SaveMemos();
            for (int i = 0; i < 4; i++) UnregisterHotKey(this.Handle, i + 1);
            RemoveClipboardFormatListener(this.Handle);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        // ---- Clipboard persistence ----
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

        // ---- Memo persistence ----
        void SaveMemos()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                StringBuilder sb = new StringBuilder();
                foreach (MemoEntry m in _memos)
                {
                    string keyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Key ?? ""));
                    string valB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Value ?? ""));
                    sb.AppendLine(keyB64 + "=" + valB64);
                }
                File.WriteAllText(MemoFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
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
                    string keyB64 = line.Substring(0, eq);
                    string valB64 = line.Substring(eq + 1);
                    try
                    {
                        string k = Encoding.UTF8.GetString(Convert.FromBase64String(keyB64));
                        string v = Encoding.UTF8.GetString(Convert.FromBase64String(valB64));
                        _memos.Add(new MemoEntry { Key = k, Value = v });
                    }
                    catch { }
                }
            }
            catch { }
            RefreshMemoList("");
        }

        // ---- Icon drawn at runtime ----
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
