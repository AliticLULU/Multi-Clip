using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        Button _okBtn;
        Button _cancelBtn;

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
            this.ClientSize = new Size(460, 270);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.White;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 4;
            root.Padding = new Padding(16, 14, 16, 12);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // ---- Row 0: Key ----
            Label keyLabel = new Label();
            keyLabel.Text = "Key";
            keyLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            keyLabel.TextAlign = ContentAlignment.MiddleRight;
            keyLabel.Padding = new Padding(0, 0, 8, 0);
            keyLabel.Dock = DockStyle.Fill;
            root.Controls.Add(keyLabel, 0, 0);

            _keyBox = new TextBox();
            _keyBox.Font = new Font("Consolas", 9.5f);
            _keyBox.Dock = DockStyle.Fill;
            _keyBox.Text = initialKey ?? "";
            _keyBox.Margin = new Padding(0, 0, 0, 2);
            root.Controls.Add(_keyBox, 1, 0);

            // ---- Row 1: Value ----
            Label valLabel = new Label();
            valLabel.Text = "Value";
            valLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            valLabel.TextAlign = ContentAlignment.TopRight;
            valLabel.Padding = new Padding(0, 5, 8, 0);
            valLabel.Dock = DockStyle.Fill;
            root.Controls.Add(valLabel, 0, 1);

            _valueBox = new TextBox();
            _valueBox.Multiline = true;
            _valueBox.Font = new Font("Consolas", 9.5f);
            _valueBox.ScrollBars = ScrollBars.Vertical;
            _valueBox.AcceptsTab = true;
            _valueBox.Dock = DockStyle.Fill;
            _valueBox.Text = initialValue ?? "";
            root.Controls.Add(_valueBox, 1, 1);

            // ---- Row 2: spacer ----

            // ---- Row 3: Buttons (using a FlowLayoutPanel for robust positioning) ----
            FlowLayoutPanel btnRow = new FlowLayoutPanel();
            btnRow.Dock = DockStyle.Fill;
            btnRow.FlowDirection = FlowDirection.RightToLeft;
            btnRow.Padding = new Padding(0, 0, 0, 0);

            _cancelBtn = new Button();
            _cancelBtn.Text = "Cancel";
            _cancelBtn.Size = new Size(80, 30);
            _cancelBtn.Font = new Font("Segoe UI", 9f);
            _cancelBtn.FlatStyle = FlatStyle.Flat;
            _cancelBtn.BackColor = Color.FromArgb(240, 240, 240);
            _cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            _cancelBtn.DialogResult = DialogResult.Cancel;
            _cancelBtn.UseVisualStyleBackColor = false;
            btnRow.Controls.Add(_cancelBtn);

            _okBtn = new Button();
            _okBtn.Text = "Save";
            _okBtn.Size = new Size(80, 30);
            _okBtn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _okBtn.FlatStyle = FlatStyle.Flat;
            _okBtn.BackColor = Color.FromArgb(0, 120, 215);
            _okBtn.ForeColor = Color.White;
            _okBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 180);
            _okBtn.DialogResult = DialogResult.OK;
            _okBtn.UseVisualStyleBackColor = false;
            btnRow.Controls.Add(_okBtn);

            root.Controls.Add(btnRow, 1, 3);

            this.AcceptButton = _okBtn;
            this.CancelButton = _cancelBtn;
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
                        "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        // ---- Memo state ----
        ListView _memoList;
        TextBox _searchBox;
        Button _addBtn;
        Button _editBtn;
        Button _delBtn;
        List<MemoEntry> _memos = new List<MemoEntry>();
        bool _memoDirty = false;

        // ---- Tray & persistence ----
        NotifyIcon _tray;
        System.Windows.Forms.Timer _saveTimer;
        Icon _appIcon;
        ToolStripMenuItem _startupItem;
        bool _reallyExit = false;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppRegName = "TetraClip";

        string DataDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); }
        }
        string MemoFile { get { return Path.Combine(DataDir, "memos.dat"); } }

        // ---- Color scheme ----
        static readonly Color AccentBlue = Color.FromArgb(0, 120, 215);
        static readonly Color BgLight = Color.FromArgb(248, 248, 248);
        static readonly Color BorderGray = Color.FromArgb(220, 220, 220);

        public MainForm()
        {
            _appIcon = BuildIcon();
            this.Icon = _appIcon;
            this.Text = "MemoClip";
            this.ClientSize = new Size(520, 440);
            this.MinimumSize = new Size(360, 260);
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.White;
            PositionBottomRight();

            BuildUI();
            LoadMemos();
            SetupTray();

            this.Load += OnLoaded;
            this.FormClosing += OnFormClosing;

            _saveTimer = new System.Windows.Forms.Timer();
            _saveTimer.Interval = 1500;
            _saveTimer.Tick += delegate
            {
                if (_memoDirty) { SaveMemos(); _memoDirty = false; }
            };
            _saveTimer.Start();

            this.KeyPreview = true;
            this.KeyDown += OnMainFormKeyDown;
        }

        void OnLoaded(object sender, EventArgs e)
        {
            PositionBottomRight();
            _searchBox.Focus();
        }

        // ---- UI: clean single-purpose memo manager ----
        void BuildUI()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(10, 10, 10, 8);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.BackColor = Color.White;
            this.Controls.Add(root);

            // ---- Row 0: Search bar + action buttons ----
            // Use a TableLayoutPanel for robust button positioning
            TableLayoutPanel topBar = new TableLayoutPanel();
            topBar.Dock = DockStyle.Fill;
            topBar.ColumnCount = 5;
            topBar.RowCount = 1;
            topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // search
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));   // gap
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));  // add
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));   // gap
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));  // edit+del panel
            topBar.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(topBar, 0, 0);

            // Search box
            Panel searchWrap = new Panel();
            searchWrap.Dock = DockStyle.Fill;
            searchWrap.BackColor = Color.White;
            searchWrap.Padding = new Padding(0);

            _searchBox = new TextBox();
            _searchBox.Font = new Font("Segoe UI", 10f);
            _searchBox.Location = new Point(1, 4);
            _searchBox.Size = new Size(100, 24); // will be anchored
            _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.TextChanged += OnSearchChanged;

            // Placeholder
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

            searchWrap.Controls.Add(_searchBox);
            topBar.Controls.Add(searchWrap, 0, 0);

            // Gap column (index 1)
            topBar.Controls.Add(new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 1, 0);

            // Add button
            _addBtn = new Button();
            _addBtn.Text = "➕ Add";
            _addBtn.Dock = DockStyle.Fill;
            _addBtn.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            _addBtn.FlatStyle = FlatStyle.Flat;
            _addBtn.BackColor = AccentBlue;
            _addBtn.ForeColor = Color.White;
            _addBtn.FlatAppearance.BorderSize = 0;
            _addBtn.UseVisualStyleBackColor = false;
            _addBtn.Cursor = Cursors.Hand;
            _addBtn.Click += delegate { AddMemo(); };
            topBar.Controls.Add(_addBtn, 2, 0);

            // Gap column (index 3)
            topBar.Controls.Add(new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 3, 0);

            // Edit + Delete buttons in a small panel
            Panel editDelPanel = new Panel();
            editDelPanel.Dock = DockStyle.Fill;
            topBar.Controls.Add(editDelPanel, 4, 0);

            _editBtn = new Button();
            _editBtn.Text = "✎ Edit";
            _editBtn.Size = new Size(82, 16);
            _editBtn.Location = new Point(0, 0);
            _editBtn.Font = new Font("Segoe UI", 9f);
            _editBtn.FlatStyle = FlatStyle.Flat;
            _editBtn.BackColor = Color.White;
            _editBtn.FlatAppearance.BorderColor = BorderGray;
            _editBtn.UseVisualStyleBackColor = false;
            _editBtn.Cursor = Cursors.Hand;
            _editBtn.Click += delegate { EditMemo(); };
            editDelPanel.Controls.Add(_editBtn);

            _delBtn = new Button();
            _delBtn.Text = "✕ Del";
            _delBtn.Size = new Size(82, 16);
            _delBtn.Location = new Point(0, 20);
            _delBtn.Font = new Font("Segoe UI", 9f);
            _delBtn.FlatStyle = FlatStyle.Flat;
            _delBtn.BackColor = Color.White;
            _delBtn.ForeColor = Color.FromArgb(200, 60, 60);
            _delBtn.FlatAppearance.BorderColor = BorderGray;
            _delBtn.UseVisualStyleBackColor = false;
            _delBtn.Cursor = Cursors.Hand;
            _delBtn.Click += delegate { DeleteMemo(); };
            editDelPanel.Controls.Add(_delBtn);

            // ---- Row 1: Memo list ----
            _memoList = new ListView();
            _memoList.Dock = DockStyle.Fill;
            _memoList.View = View.Details;
            _memoList.FullRowSelect = true;
            _memoList.GridLines = true;
            _memoList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _memoList.Font = new Font("Consolas", 9.5f);
            _memoList.BorderStyle = BorderStyle.FixedSingle;
            _memoList.BackColor = Color.White;
            _memoList.ForeColor = Color.FromArgb(40, 40, 40);
            _memoList.OwnerDraw = false;
            _memoList.Columns.Add("Key", 150, HorizontalAlignment.Left);
            _memoList.Columns.Add("Value", 300, HorizontalAlignment.Left);
            _memoList.DoubleClick += delegate { EditMemo(); };
            _memoList.KeyDown += OnMemoListKeyDown;
            _memoList.ColumnWidthChanging += OnColumnWidthChanging;
            _memoList.SizeChanged += OnMemoListSizeChanged;
            _memoList.Margin = new Padding(0, 0, 0, 4);
            root.Controls.Add(_memoList, 0, 1);

            // ---- Row 2: Status bar ----
            Panel statusBar = new Panel();
            statusBar.Dock = DockStyle.Fill;
            statusBar.BackColor = Color.FromArgb(245, 245, 245);
            statusBar.Padding = new Padding(6, 2, 6, 0);

            Label statusLabel = new Label();
            statusLabel.Text = "Ready";
            statusLabel.Font = new Font("Segoe UI", 7.5f);
            statusLabel.ForeColor = Color.FromArgb(140, 140, 140);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Name = "statusLabel";
            statusBar.Controls.Add(statusLabel);

            root.Controls.Add(statusBar, 0, 2);
        }

        void OnMemoListSizeChanged(object sender, EventArgs e)
        {
            AdjustColumns();
        }

        void OnColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            // Let the user resize columns freely
        }

        void AdjustColumns()
        {
            if (_memoList.Columns.Count < 2) return;
            int total = _memoList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
            if (total < 100) return;
            // Keep existing ratio if columns already have non-default widths
            int keyW = Math.Max(100, total * 35 / 100);
            int valW = total - keyW;
            _memoList.BeginUpdate();
            _memoList.Columns[0].Width = keyW;
            _memoList.Columns[1].Width = valW;
            _memoList.EndUpdate();
        }

        void OnSearchChanged(object sender, EventArgs e)
        {
            if (_searchBox.ForeColor == SystemColors.GrayText) return;
            RefreshMemoList(_searchBox.Text);
            UpdateStatusBar();
        }

        void OnMemoListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                DeleteMemo();
        }

        void OnMainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                AddMemo();
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                e.SuppressKeyPress = true;
                _searchBox.Focus();
                _searchBox.SelectAll();
            }
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
            AdjustColumns();
        }

        void AddMemo()
        {
            MemoEditForm dlg = new MemoEditForm("Add Memo", "", "");
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show(this, "Please enter a key name for this memo.",
                        "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    dlg.Dispose();
                    return;
                }
                _memos.Add(new MemoEntry { Key = dlg.MemoKey, Value = dlg.MemoValue });
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
                UpdateStatusBar();
            }
            dlg.Dispose();
        }

        void EditMemo()
        {
            if (_memoList.SelectedItems.Count == 0)
            {
                // No selection — if there are items, show a hint
                if (_memoList.Items.Count > 0)
                {
                    MessageBox.Show(this, "Please select a memo to edit (click on it first).",
                        "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            ListViewItem item = _memoList.SelectedItems[0];
            MemoEntry orig = item.Tag as MemoEntry;
            if (orig == null) return;

            MemoEditForm dlg = new MemoEditForm("Edit Memo", orig.Key, orig.Value);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(dlg.MemoKey))
                {
                    MessageBox.Show(this, "Please enter a key name for this memo.",
                        "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    dlg.Dispose();
                    return;
                }
                orig.Key = dlg.MemoKey;
                orig.Value = dlg.MemoValue;
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
                UpdateStatusBar();
            }
            dlg.Dispose();
        }

        void DeleteMemo()
        {
            if (_memoList.SelectedItems.Count == 0)
            {
                if (_memoList.Items.Count > 0)
                {
                    MessageBox.Show(this, "Please select a memo to delete (click on it first).",
                        "MemoClip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            ListViewItem item = _memoList.SelectedItems[0];
            MemoEntry entry = item.Tag as MemoEntry;
            if (entry == null) return;

            string keyPreview = (entry.Key ?? "").Length > 50
                ? (entry.Key ?? "").Substring(0, 50) + "..."
                : (entry.Key ?? "");

            DialogResult dr = MessageBox.Show(this,
                "Delete memo \"" + keyPreview + "\"?\n\nThis cannot be undone.",
                "MemoClip",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                _memos.Remove(entry);
                _memoDirty = true;
                RefreshMemoList(GetSearchText());
                UpdateStatusBar();
            }
        }

        string GetSearchText()
        {
            if (_searchBox.ForeColor == SystemColors.GrayText) return "";
            return _searchBox.Text ?? "";
        }

        void UpdateStatusBar()
        {
            // Find the status label in the status bar
            foreach (Control c in this.Controls)
            {
                TableLayoutPanel root = c as TableLayoutPanel;
                if (root == null) continue;
                if (root.RowCount >= 3)
                {
                    Control bottom = root.GetControlFromPosition(0, 2);
                    if (bottom is Panel)
                    {
                        foreach (Control lbl in bottom.Controls)
                        {
                            if (lbl is Label && lbl.Name == "statusLabel")
                            {
                                string filter = GetSearchText();
                                int showing = _memoList.Items.Count;
                                int total = _memos.Count;
                                if (string.IsNullOrEmpty(filter))
                                    lbl.Text = showing + " memo" + (showing != 1 ? "s" : "");
                                else
                                    lbl.Text = "Showing " + showing + " of " + total + " memo" + (total != 1 ? "s" : "");
                                return;
                            }
                        }
                    }
                }
            }
        }

        // ---- Tray + window ----
        void SetupTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "MemoClip — Key-Value Memos";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Show / Hide", null, delegate { ToggleWindow(); }));
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
                _tray.BalloonTipTitle = "MemoClip";
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
            SaveMemos();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

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
            UpdateStatusBar();
        }

        // ---- Icon drawn at runtime ----
        Icon BuildIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Draw a simple memo icon: a notepad with lines
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    g.FillRectangle(bg, 5, 2, 22, 26);

                // Notepad fold
                Point[] fold = {
                    new Point(27, 2),
                    new Point(27, 10),
                    new Point(19, 2)
                };
                using (SolidBrush foldBr = new SolidBrush(Color.FromArgb(0, 90, 180)))
                    g.FillPolygon(foldBr, fold);

                // Lines on the notepad
                using (Pen linePen = new Pen(Color.FromArgb(255, 255, 255, 80)))
                {
                    for (int y = 8; y <= 22; y += 5)
                        g.DrawLine(linePen, 9, y, 23, y);
                }
            }
            IntPtr hicon = bmp.GetHicon();
            Icon ic = Icon.FromHandle(hicon);
            bmp.Dispose();
            return ic;
        }
    }
}
