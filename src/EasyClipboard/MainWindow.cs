using EasyClipboard.HotspotProbe;

namespace EasyClipboard;

internal sealed class MainWindow : Form
{
    private static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control :
        Program.LightTheme ? Color.FromArgb(226, 226, 226) : Color.FromArgb(29, 29, 29);
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window :
        Program.LightTheme ? Color.FromArgb(243, 243, 243) : Color.Black;
    private static Color AppearanceSurface => SystemInformation.HighContrast || Program.LightTheme ? Surface : Color.FromArgb(34, 34, 34);
    internal static Color TextColor => SystemInformation.HighContrast ? SystemColors.WindowText : Program.LightTheme ? Color.FromArgb(25, 25, 25) : Color.White;
    internal static Color SelectionColor => Program.LightTheme ? Color.FromArgb(213, 213, 213) : Color.FromArgb(48, 48, 48);
    internal bool RestartRequested { get; private set; }
    internal bool RebuildRequested { get; private set; }
    internal bool ResumeReceivingAfterRestart { get; private set; }
    private readonly Identity _identity;
    private readonly HistoryBank _bank;
    private readonly HotspotHost _hotspot;
    private readonly WindowsClipboard _clipboard;
    private readonly TransferService _transfers;
    private readonly ClipboardExport _clipboardExport;
    // Not shown; tracks whether fetching is live this session. The user's choice lives in Settings (_fetchAllowed).
    private readonly CheckBox _allowFetch = new() { Enabled = false };
    private bool _fetchAllowed;
    private ReceiverServer? _server;
    private HotspotBinding? _binding;
    private readonly bool _preview;
    private readonly bool _demo;
    private bool _demoReceiving;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _interfaces = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly Button _refresh = IconButton("Refresh networks");
    private readonly Label _tailscaleStatus = new() { AutoSize = true, Margin = new Padding(4, 5, 0, 0) };
    private bool HomeMode => _mode.SelectedIndex != 2;
    private bool HomeHttp => _mode.SelectedIndex == 0;
    private bool TailscaleMode => _mode.SelectedIndex == 3;
    private bool HttpTransport => HomeHttp || TailscaleMode;
    private const string DeveloperModePassword = "admin123";
    private readonly string _rememberDeveloperPath;
    private bool _developerUnlocked;
    private bool _checkingUpdates;
    private readonly NotifyIcon _tray;
    private readonly HistoryList _items = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _imagesOnly = new() { Text = "Images", AutoSize = true };
    private readonly RadioButton _textOnly = new() { Text = "Text", AutoSize = true };
    private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly PictureBox _image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = Surface };
    private readonly Label _status = new() { Text = "Stopped. Pick your network, then press ▶.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _connection = new() { Text = "Phone requests: off.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox _setup = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 145 };
    // _start is the play/pause toggle: Start when stopped, then Pause / Resume while receiving.
    private readonly Button _start = IconButton("Start receiving");
    private readonly Button _stop = IconButton("Stop receiving");
    private readonly StatusDot _dot = new() { Margin = new Padding(4, 0, 2, 0) };
    private readonly Label _state = new() { Text = "Stopped", AutoSize = true, Margin = Padding.Empty };
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _blink = new() { Interval = 600 };
    private readonly CalendarView _calendar;
    private Action? _startTour;
    private Tour? _tour;
    private readonly CheckBox _pause = new ReadableCheckBox { Text = "Pause receiving", AutoSize = true, Padding = new Padding(6), Enabled = false };
    private readonly ToolStripMenuItem _trayPause = new("Pause receiving") { CheckOnClick = true, Enabled = false };
    private readonly ToolStripMenuItem _trayRestart = new("Restart EasyClipboard");
    private bool _quitting;
    private bool _closed;
    private bool _starting;
    private bool _refreshingHistory;
    private Guid? _previewedId;
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private int _stopRequests;

    public MainWindow(string? dataRoot = null, bool startReceiving = false, bool demo = false, bool tour = false)
    {
        Text = demo ? "EasyClipboard Demo" : "EasyClipboard";
        ClientSize = new Size(970, 750);
        MinimumSize = new Size(760, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Canvas;
        Icon = Icon.ExtractIcon(Application.ExecutablePath, 0, 48) ?? SystemIcons.Application;
        var brandImage = Icon.ToBitmap();
        // Bottom status lines read as labels: no closing period, whichever message is shown.
        foreach (var line in new[] { _status, _connection })
        {
            line.TextChanged += (_, _) => { if (line.Text.EndsWith('.')) line.Text = line.Text.TrimEnd('.'); };
            line.Text = line.Text.TrimEnd('.'); // the starting text was set before this hook
        }
        Disposed += (_, _) => brandImage.Dispose();
        StyleTextBox(_setup);
        StyleTextBox(_text);
        foreach (var combo in new[] { _mode, _interfaces })
        {
            if (SystemInformation.HighContrast) continue;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Surface;
            combo.ForeColor = TextColor;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var selected = (e.State & DrawItemState.Selected) != 0;
                using var background = new SolidBrush(selected ? SelectionColor : Surface);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), e.Font,
                    Rectangle.Inflate(e.Bounds, -2, 0), combo.Enabled ? TextColor : DisabledText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
        }
        _preview = dataRoot is not null;
        _demo = demo;
        _identity = new Identity(dataRoot ?? LocalFiles.DefaultRoot);
        _bank = new HistoryBank(dataRoot ?? LocalFiles.DefaultRoot);
        _rememberDeveloperPath = Path.Combine(_bank.Root, "remember-developer-mode");
        _developerUnlocked = File.Exists(_rememberDeveloperPath);
        _fetchAllowed = !File.Exists(Path.Combine(_bank.Root, "fetch-disabled")); // on by default
        _hotspot = new HotspotHost(false) { Ssid = _identity.Ssid, Password = _identity.WifiPassword };
        _clipboard = new WindowsClipboard(this);
        _transfers = new TransferService(_bank, _clipboard);
        _clipboardExport = new ClipboardExport(_clipboard);
        _transfers.Changed += () => Ui(() => { _status.Text = "Received and copied. Ready to paste."; RefreshHistory(); });
        _hotspot.StatusChanged += message => Ui(() => _status.Text = message);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 26), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        // Decorative edges live in the window margin (the layout's padding band).
        var edgeFile = Path.Combine(_bank.Root, "edges.txt");
        var edgeStyle = File.Exists(edgeFile) ? File.ReadAllText(edgeFile).Trim() : "None";
        Control? sideArtStart = null; // the tab row: side art runs beside the content box, below the tabs
        layout.Paint += (_, e) => EdgeArt.Draw(e.Graphics, layout.ClientRectangle, layout.Padding, edgeStyle, sideArtStart?.Bottom ?? 0);
        layout.Resize += (_, _) => layout.Invalidate();
        // Home, calendar and customize share the body cell; only one is visible.
        var home = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        home.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
        home.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        home.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        foreach (var width in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize })
            header.ColumnStyles.Add(new ColumnStyle(width, 100));
        var logo = new PictureBox { Image = brandImage, Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = Padding.Empty, Cursor = Cursors.Hand };
        header.Controls.Add(logo, 0, 0);
        var titleFont = new Font(Font, FontStyle.Bold);
        Disposed += (_, _) => titleFont.Dispose();
        var title = new Label { Text = "EasyClipboard", Font = titleFont, AutoSize = true, Margin = new Padding(6, 0, 0, 2), Cursor = Cursors.Hand, AccessibleRole = AccessibleRole.Link };
        var state = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        state.Controls.AddRange([_dot, _state]);
        var titleStack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        titleStack.Controls.AddRange([title, state]);
        header.Controls.Add(titleStack, 1, 0);
        var settings = IconButton("Settings", CreateSettingsIcon(LogicalToDeviceUnits(22)));
        settings.Click += (_, _) => ShowSettings();
        _start.Anchor = _stop.Anchor = AnchorStyles.Left;
        _start.Margin = new Padding(22, 0, 0, 0);
        header.Controls.Add(_start, 2, 0);
        header.Controls.Add(_stop, 3, 0);
        header.Controls.Add(settings, 5, 0);
        foreach (var button in new[] { _start, _stop, settings, _refresh }) _tips.SetToolTip(button, button.AccessibleName);
        layout.Controls.Add(header);
        Load += (_, _) => _dot.Size = new Size(LogicalToDeviceUnits(14), _state.Height); // same line height as the text, so it centers
        // Blink between full and dimmed green so the dot never disappears.
        var dimStart = Color.FromArgb((StartColor.R + Canvas.R * 2) / 3, (StartColor.G + Canvas.G * 2) / 3, (StartColor.B + Canvas.B * 2) / 3);
        _blink.Tick += (_, _) => _dot.ForeColor = _dot.ForeColor == StartColor ? dimStart : StartColor;
        var network = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _mode.Items.AddRange(["Home Wi-Fi / LAN (HTTP)", "Home Wi-Fi / LAN (HTTPS)", "Private hotspot (HTTPS)", "Tailscale (encrypted tunnel)"]);
        _mode.SelectedIndex = 3;
        _mode.Visible = _developerUnlocked;
        _interfaces.Visible = !_demo;
        var pairing = Button("Pairing / setup");
        pairing.Click += (_, _) => ShowPairing();
        _refresh.Image = CreateRefreshIcon(LogicalToDeviceUnits(18));
        _refresh.Margin = new Padding(0, 3, 6, 8);
        network.Controls.AddRange([_mode, _interfaces, _refresh, _tailscaleStatus, pairing]);
        layout.Controls.Add(network);
        // Tab row: fetch toggle on the left, view tabs on the right, touching the body below.
        var calendarTab = new WrapTab { Icon = CreateCalendarIcon(LogicalToDeviceUnits(22)), AccessibleName = "Calendar", ActiveColor = CalendarView.HeaderColor };
        var shopTab = new WrapTab { Icon = CreateCartIcon(LogicalToDeviceUnits(22)), AccessibleName = "Customize", ActiveColor = CalendarView.Page };
        // Home's wrap joins the setup pane, so it uses that pane's surface color.
        var homeTab = new WrapTab { Icon = CreateHomeIcon(LogicalToDeviceUnits(22)), AccessibleName = "Home", ActiveColor = Surface, Active = true, FlushLeft = true };
        _tips.SetToolTip(calendarTab, "Calendar");
        _tips.SetToolTip(shopTab, "Customize");
        _tips.SetToolTip(homeTab, "Home");
        // Home sits flush with the left edge so its wrap runs straight down into the panel.
        var tabRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 2, 0, 0) };
        foreach (var width in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent })
            tabRow.ColumnStyles.Add(new ColumnStyle(width, 100));
        tabRow.Controls.Add(homeTab, 0, 0);
        tabRow.Controls.Add(calendarTab, 1, 0);
        tabRow.Controls.Add(shopTab, 2, 0);
        layout.Controls.Add(tabRow);
        sideArtStart = tabRow;
        tabRow.LocationChanged += (_, _) => layout.Invalidate();
        // FlowLayoutPanel top-aligns its row; center everything on the Pairing button instead.
        Load += (_, _) =>
        {
            foreach (Control control in network.Controls)
                if (control != pairing)
                    control.Margin = control.Margin with { Top = pairing.Margin.Top + (pairing.Height - control.Height) / 2 };
        };
        _refresh.Click += (_, _) => RefreshInterfaces();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _interfaces.Enabled = _refresh.Enabled = HomeMode;
            RefreshInterfaces();
            ShowStoppedSetup();
        };
        _pause.CheckedChanged += (_, _) => { _transfers.Paused = _pause.Checked; UpdateReceiverControls(); };
        _allowFetch.CheckedChanged += (_, _) => _clipboardExport.Enabled = _allowFetch.Checked;
        home.Controls.Add(new RoundedPane(_setup) { Margin = new Padding(0, 0, 0, 3), SquareTopLeft = true }); // joins the Home tab
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 350), SplitterDistance = 350 };
        var history = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Surface };
        history.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        history.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var all = new RadioButton { Text = "All", AutoSize = true, Checked = true };
        foreach (var filter in new[] { all, _imagesOnly, _textOnly })
        {
            if (!SystemInformation.HighContrast)
            {
                filter.Appearance = Appearance.Button;
                filter.FlatStyle = FlatStyle.Flat;
                filter.UseVisualStyleBackColor = false;
                filter.FlatAppearance.BorderSize = 0;
                filter.FlatAppearance.CheckedBackColor = SelectionColor;
                filter.ForeColor = TextColor;
                RoundButtonCorners(filter);
            }
            filter.CheckedChanged += (_, _) =>
            {
                if (filter.Checked) RefreshHistory();
            };
            filters.Controls.Add(filter);
        }
        history.Controls.Add(filters, 0, 0);
        history.Controls.Add(_items, 0, 1);
        split.SplitterWidth = 8;
        split.Panel1.Controls.Add(new RoundedPane(history));
        split.Panel2.Controls.Add(new RoundedPane(_text, _image));
        home.Controls.Add(split);
        // Item actions on the left, bulk clears pushed to the right.
        var historyActions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        foreach (var width in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
            historyActions.ColumnStyles.Add(new ColumnStyle(width, 100));
        var copy = Button("Copy Again");
        copy.Click += async (_, _) => { if (_items.SelectedItem is HistoryEntry entry) await CopyEntryAsync(entry); };
        async Task CopyEntryAsync(HistoryEntry entry)
        {
            try
            {
                var item = _bank.Find(entry.Id);
                if (item is null) { RefreshHistory(); return; }
                await _clipboard.ApplyAsync(item, () => { });
                _status.Text = "Copied. Ready to paste.";
            }
            catch (Exception) { _status.Text = "Couldn't copy that item. Try again."; }
        }
        copy.Font = titleFont; // bold: the main history action
        var delete = Button("Delete");
        // Muted red: marks the destructive action without shouting.
        if (!SystemInformation.HighContrast)
            delete.ForeColor = Program.LightTheme ? Color.FromArgb(166, 66, 66) : Color.FromArgb(222, 140, 140);
        delete.Click += (_, _) => Guard(() => { if (_items.SelectedItem is HistoryEntry item) _bank.Delete(item.Id); RefreshHistory(); });
        var clear = Button("Clear history");
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Delete all locally saved clipboard entries?", "Clear history", MessageBoxButtons.YesNo) == DialogResult.Yes)
                Guard(() => { _bank.Clear(); RefreshHistory(); });
        };
        var duplicates = Button("Clear duplicates");
        duplicates.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Remove identical saved entries and keep the newest copy of each?",
                "Clear duplicates", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            duplicates.Enabled = false;
            try
            {
                var removed = await Task.Run(_bank.ClearDuplicates);
                _status.Text = removed switch
                {
                    0 => "No duplicate entries found.",
                    1 => "Removed 1 duplicate entry.",
                    _ => $"Removed {removed} duplicate entries."
                };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _status.Text = "Couldn't remove duplicates. Check folder permissions."; }
            finally
            {
                if (!IsDisposed)
                {
                    RefreshHistory();
                    duplicates.Enabled = true;
                }
            }
        };
        clear.Margin = clear.Margin with { Right = 0 };
        historyActions.Controls.Add(copy, 0, 0);
        historyActions.Controls.Add(delete, 1, 0);
        historyActions.Controls.Add(duplicates, 3, 0);
        historyActions.Controls.Add(clear, 4, 0);
        home.Controls.Add(historyActions);
        _calendar = new CalendarView(id => _bank.Find(id)) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var weekLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, BackColor = CalendarView.HeaderColor, ForeColor = TextColor, Font = titleFont, Margin = new Padding(10, 0, 12, 0) };
        _calendar.WeekChanged += () => weekLabel.Text = $"{_calendar.WeekStart:MMM d} - {_calendar.WeekStart.AddDays(6):MMM d, yyyy}";
        var previous = IconButton("Previous week", CreateChevronIcon(LogicalToDeviceUnits(18), left: true));
        var next = IconButton("Next week", CreateChevronIcon(LogicalToDeviceUnits(18), left: false));
        var today = Button("This week");
        today.Anchor = AnchorStyles.Right;
        today.Margin = new Padding(0, 0, 4, 0);
        _tips.SetToolTip(previous, previous.AccessibleName);
        _tips.SetToolTip(next, next.AccessibleName);
        previous.Click += (_, _) => _calendar.ShowWeek(_calendar.WeekStart.AddDays(-7));
        next.Click += (_, _) => _calendar.ShowWeek(_calendar.WeekStart.AddDays(7));
        today.Click += (_, _) => _calendar.ShowWeek(DateTime.Today, selectDay: true);
        var weekNav = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = CalendarView.HeaderColor, Padding = new Padding(8, 8, 8, 0), ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        foreach (var width in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize })
            weekNav.ColumnStyles.Add(new ColumnStyle(width, 100));
        weekNav.Controls.Add(previous, 0, 0);
        weekNav.Controls.Add(next, 1, 0);
        weekNav.Controls.Add(weekLabel, 2, 0);
        weekNav.Controls.Add(today, 4, 0);
        // Full black or white so the calendar stands apart from the gray window.
        var calendarPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CalendarView.Page, Margin = Padding.Empty, Visible = false };
        calendarPage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        calendarPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        calendarPage.Controls.Add(weekNav, 0, 0);
        calendarPage.Controls.Add(_calendar, 0, 1);
        var customizePage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = CalendarView.Page, Margin = Padding.Empty, Padding = new Padding(20, 16, 20, 16), Visible = false };
        customizePage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        customizePage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        customizePage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        customizePage.Controls.Add(new Label { Text = "Edges", Font = titleFont, AutoSize = true, ForeColor = TextColor, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
        customizePage.Controls.Add(new Label { Text = "Decorate the sides and bottom of the window. More styles can be added here later.", AutoSize = true, ForeColor = DisabledText, Margin = new Padding(0, 0, 0, 12) }, 0, 1);
        var edgeTiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CalendarView.Page, Margin = Padding.Empty };
        foreach (var style in EdgeArt.Styles)
        {
            var tile = new EdgeTile(style) { Selected = style == edgeStyle };
            tile.Click += (_, _) =>
            {
                try { LocalFiles.Write(edgeFile, System.Text.Encoding.UTF8.GetBytes(style)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { _status.Text = "Couldn't save the edge style."; return; }
                edgeStyle = style;
                foreach (EdgeTile other in edgeTiles.Controls) other.Selected = other == tile;
                layout.Invalidate();
            };
            edgeTiles.Controls.Add(tile);
        }
        customizePage.Controls.Add(edgeTiles, 0, 2);
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        body.Controls.AddRange([home, calendarPage, customizePage]);
        layout.Controls.Add(body);
        void ShowPage(Control page)
        {
            if (page == calendarPage && !calendarPage.Visible) _calendar.ShowWeek(DateTime.Today, selectDay: true);
            foreach (Control view in body.Controls) view.Visible = view == page;
            calendarTab.Active = page == calendarPage;
            shopTab.Active = page == customizePage;
            homeTab.Active = page == home;
            if (page == calendarPage) _calendar.Focus();
        }
        calendarTab.Click += (_, _) => ShowPage(calendarPage.Visible ? home : calendarPage);
        shopTab.Click += (_, _) => ShowPage(customizePage.Visible ? home : customizePage);
        homeTab.Click += (_, _) => ShowPage(home);
        // Double-click a calendar card to see it full size in the home preview.
        _calendar.Activated += entry =>
        {
            ShowPage(home);
            all.Checked = true; // the entry must be in the list to preview it
            _items.Select(entry.Id);
        };
        title.Click += (_, _) => ShowPage(home);
        logo.Click += (_, _) => ShowPage(home);
        layout.Controls.Add(_status);
        layout.Controls.Add(_connection);
        _items.SelectedIndexChanged += (_, _) => Preview();
        VisibleChanged += (_, _) => Preview();
        Resize += (_, _) => Preview();
        _start.Click += async (_, _) =>
        {
            if (_server is not null || _demoReceiving) _pause.Checked = !_pause.Checked;
            else await StartAsync();
        };
        _stop.Click += async (_, _) => await StopAsync();
        _stop.Enabled = false;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open history", null, (_, _) => OpenWindow());
        _trayPause.CheckedChanged += (_, _) => _pause.Checked = _trayPause.Checked;
        _pause.CheckedChanged += (_, _) => _trayPause.Checked = _pause.Checked;
        menu.Items.Add(_trayPause);
        if (!_preview)
        {
            var startupFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "EasyClipboard.cmd");
            var startup = new ToolStripMenuItem("Start with Windows") { Checked = File.Exists(startupFile) };
            startup.Click += (_, _) =>
            {
                Guard(() =>
                {
                    if (File.Exists(startupFile))
                        File.Delete(startupFile);
                    else
                    {
                        var executable = Path.Combine(AppContext.BaseDirectory, "EasyClipboard.exe").Replace("%", "%%");
                        var runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                        var runtimeLine = string.IsNullOrEmpty(runtime) ? "" : $"set \"DOTNET_ROOT={runtime.Replace("%", "%%")}\"\r\n";
                        LocalFiles.Write(startupFile, System.Text.Encoding.Default.GetBytes(
                            $"@echo off\r\nsetlocal\r\n{runtimeLine}start \"\" \"{executable}\" --start-receiving\r\n"));
                    }
                });
                startup.Checked = File.Exists(startupFile);
            };
            menu.Items.Add(startup);
        }
        var taildropSetting = Path.Combine(_bank.Root, "taildrop-import.enabled");
        var taildrop = new ToolStripMenuItem("Import temporary Taildrop images")
        { CheckOnClick = true, Checked = File.Exists(taildropSetting), Enabled = !_preview };
        menu.Items.Add(taildrop);
        taildrop.CheckedChanged += (_, _) => Guard(() =>
        {
            if (taildrop.Checked) LocalFiles.Write(taildropSetting, []);
            else File.Delete(taildropSetting);
        });
        var importer = new TaildropImport(_bank, _transfers);
        var taildropTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        Task? importTask = null;
        async Task ImportTaildropAsync()
        {
            try
            {
                var enabled = taildrop.Checked;
                var result = await Task.Run(() => importer.RunAsync(enabled));
                if (result.Expired) RefreshHistory();
                if (result.Warning is { } warning) _status.Text = warning;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            { _status.Text = "Couldn't check Taildrop images. Check folder permissions."; }
        }
        taildropTimer.Tick += (_, _) =>
        {
            if (!_quitting && importTask is not { IsCompleted: false })
                importTask = ImportTaildropAsync();
        };
        if (!_preview) taildropTimer.Start();
        menu.Items.Add("Pairing / setup", null, (_, _) => { Show(); ShowPairing(); });
        _trayRestart.Visible = _developerUnlocked;
        _trayRestart.Click += (_, _) => Restart(rebuildSource: true);
        menu.Items.Add(_trayRestart);
        menu.Items.Add("Quit", null, (_, _) => { _quitting = true; Close(); });
        _tray = new NotifyIcon { Icon = Icon, Text = "EasyClipboard", ContextMenuStrip = menu, Visible = !_preview };
        _tray.DoubleClick += (_, _) => OpenWindow();
        var networkTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        networkTimer.Tick += (_, _) =>
        {
            if (!Visible || !TailscaleMode || _starting || _stopRequests > 0) return;
            if (_server is not null) ShowTailscaleStatus(_binding?.IsPresent() == true);
            else if (!_interfaces.DroppedDown) RefreshInterfaces();
        };
        if (!_preview) networkTimer.Start();
        FormClosing += async (_, args) =>
        {
            if (_closed) return;
            args.Cancel = true;
            if (!_quitting && !_demo) { Hide(); return; }
            _quitting = true;
            Enabled = false;
            taildropTimer.Dispose();
            networkTimer.Dispose();
            _blink.Dispose();
            _tour?.Dispose();
            if (importTask is not null) await importTask;
            await StopAsync();
            _tray.Dispose();
            _identity.Dispose();
            _image.Image?.Dispose();
            _closed = true;
            Close();
        };
        RefreshInterfaces();
        ShowStoppedSetup();
        RefreshHistory();
        Shown += (_, _) => OpenWindow();
        // First-time tour: once per data folder (every launch for --demo-tour). Not in screenshot previews.
        var tourDone = Path.Combine(_bank.Root, "tour-done");
        _startTour = () =>
        {
            if (_tour is not null || _quitting) return;
            ShowPage(home);
            // Kept short on purpose: only what someone needs before their first transfer.
            _tour = new Tour(this, [
                new("Start", "Press ▶. A green dot means ready.",
                    () => [_start, _stop, titleStack]),
                new("Network", "Pick this PC's Tailscale address.",
                    () => [network]),
                new("iPhone setup", "Scan the setup code in Pairing / setup with your Settings Shortcut. These are the values it saves.",
                    () => [pairing], ShortcutValues),
                new("Done", "Everything else is in Settings, including this tour.",
                    () => [settings]),
            ], text => SettingsButton(text));
            _tour.Finished += () =>
            {
                _tour = null;
                if (!tour) try { LocalFiles.Write(tourDone, []); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            };
            _tour.Start();
        };
        if (!_preview || _demo)
            Shown += (_, _) => { if (tour || !File.Exists(tourDone)) BeginInvoke(_startTour); };
        if (startReceiving) Shown += async (_, _) => await StartAsync();
        if (!_preview) Shown += async (_, _) => await CheckForUpdateAsync(interactive: false);
    }

    internal void FinishPreview() { _quitting = true; Close(); }

    private void Restart(bool rebuildSource = false)
    {
        if (_quitting) return;
        RestartRequested = true;
        RebuildRequested = rebuildSource;
        ResumeReceivingAfterRestart = TailscaleMode && _server is not null;
        _quitting = true;
        Close();
    }

    internal void RequestActivation() => Ui(OpenWindow);

    private void OpenWindow()
    {
        if (_quitting) return;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        // Windows may refuse focus to a background launch; a TopMost blip still raises the window.
        TopMost = true;
        TopMost = false;
        Activate();
        if (_server is null && !_starting) RefreshInterfaces();
    }

    private void ShowSettings()
    {
        using var dialog = new Form
        {
            Text = "Settings", ClientSize = new Size(600, 710), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Canvas, Font = Font
        };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        var appearance = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = AppearanceSurface };
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var themes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var selectedTheme = Program.Appearance;
        var applyTheme = SettingsButton("Apply", primary: true);
        applyTheme.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        applyTheme.AccessibleDescription = "Save appearance and restart EasyClipboard.";
        applyTheme.Enabled = false;
        foreach (var name in new[] { "Dark", "Light", "System" })
        {
            var option = new AppearanceRadio
            {
                Text = name, AutoSize = true, Checked = name == Program.Appearance,
                Margin = new Padding(0, 7, 0, 7), ForeColor = TextColor
            };
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                selectedTheme = name;
                applyTheme.Enabled = name != Program.Appearance;
            };
            themes.Controls.Add(option);
        }
        applyTheme.Click += (_, _) =>
        {
            try
            {
                LocalFiles.Write(Path.Combine(_bank.Root, Program.AppearanceFile), System.Text.Encoding.UTF8.GetBytes(selectedTheme));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(dialog, "Could not save the appearance setting.", "Settings");
                return;
            }
            if (_preview && !_demo)
            {
                dialog.Close();
                return;
            }
            dialog.DialogResult = DialogResult.Retry;
        };
        appearance.Controls.Add(themes, 0, 0);
        appearance.Controls.Add(applyTheme, 1, 0);
        var storage = SettingsButton("Manage");
        storage.Click += (_, _) => ConfigureStorage(dialog);
        var updates = SettingsButton("Check now");
        updates.Enabled = !_demo;
        updates.Click += async (_, _) => await CheckForUpdateAsync(interactive: true, dialog);
        var developer = SettingsButton(_developerUnlocked ? "Lock" : "Unlock", primary: !_developerUnlocked);
        developer.Enabled = _server is null && !_demoReceiving && !_starting && _stopRequests == 0;
        developer.Click += (_, _) =>
        {
            if (!_developerUnlocked && !PromptForDeveloperPassword(dialog)) return;
            if (_developerUnlocked)
            {
                try { File.Delete(_rememberDeveloperPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(dialog, "Could not clear the remembered developer setting.", "Settings");
                    return;
                }
            }
            _developerUnlocked = !_developerUnlocked;
            _trayRestart.Visible = _developerUnlocked;
            _mode.SelectedIndex = 3;
            _mode.Visible = _developerUnlocked;
            UpdateReceiverControls();
            dialog.Close();
        };
        var fetch = SettingsButton(_fetchAllowed ? "Turn off" : "Turn on");
        fetch.Click += (_, _) =>
        {
            var file = Path.Combine(_bank.Root, "fetch-disabled");
            try { if (_fetchAllowed) LocalFiles.Write(file, []); else File.Delete(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(dialog, "Couldn't save the phone fetch setting.", "Settings");
                return;
            }
            _fetchAllowed = !_fetchAllowed;
            _allowFetch.Checked = _fetchAllowed && (_server is not null || _demoReceiving);
            fetch.Text = _fetchAllowed ? "Turn off" : "Turn on";
        };
        var rerunTour = SettingsButton("Start tour");
        rerunTour.Click += (_, _) => { dialog.Close(); BeginInvoke(() => _startTour?.Invoke()); };
        // Order: what the app does, how it looks, upkeep, help, then advanced.
        AddSettingRow("EasyClipboard",
            ("History: set limits for saved clipboard items.", storage),
            ("Phone fetch: let your phone get the PC clipboard while receiving. On by default.", fetch));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        options.Controls.Add(SettingsSection("Appearance", appearance), 0, options.Controls.Count);
        AddSettingRow("Updates", ("Check for a new EasyClipboard version.", updates));
        AddSettingRow("Tour", ("Walk through the main parts of EasyClipboard again.", rerunTour));
        AddSettingRow("Developer mode", ("Access advanced connection options. Stop receiving before changing this.", developer));
        dialog.Controls.Add(options);
        if (dialog.ShowDialog(this) == DialogResult.Retry) Restart();

        static RoundedPane SettingsSection(string title, Control contents) => new(contents)
        {
            Caption = title, AccessibleName = title, AccessibleRole = AccessibleRole.Grouping,
            Padding = new Padding(15, 24, 15, 12)
        };

        void AddSettingRow(string title, params (string Description, Control Action)[] lines)
        {
            options.RowStyles.Add(new RowStyle(SizeType.Percent, lines.Length)); // two-line sections get twice the height
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = lines.Length, BackColor = AppearanceSurface };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var index = 0; index < lines.Length; index++)
            {
                row.RowStyles.Add(new RowStyle(SizeType.Percent, 1));
                var text = new Label { AutoSize = true, Text = lines[index].Description, Anchor = AnchorStyles.Left, MaximumSize = new Size(350, 0), Margin = Padding.Empty };
                lines[index].Action.Anchor = AnchorStyles.Right;
                lines[index].Action.Margin = new Padding(12, 0, 0, 0);
                row.Controls.Add(text, 0, index);
                row.Controls.Add(lines[index].Action, 1, index);
            }
            options.Controls.Add(SettingsSection(title, row), 0, options.Controls.Count);
        }
    }

    // Tour step: which value goes into which iPhone Shortcut field, each with a Copy button.
    private Control ShortcutValues()
    {
        var baseUrl = ReceiverBaseUrl();
        var token = _identity.Token;
        (string Shortcut, string Field, string Shown, string? Copy)[] rows = _demo
            ? [("Send to Laptop", "URL", "[hidden in demo]", null),
               ("Get from PC", "URL", "[hidden in demo]", null),
               ("Both", "Authorization header", "Bearer [hidden in demo]", null)]
            : [("Send to Laptop", "URL", baseUrl is null ? "Pick a network first" : baseUrl + ReceiverServer.SendPath, baseUrl is null ? null : baseUrl + ReceiverServer.SendPath),
               ("Get from PC", "URL", baseUrl is null ? "Pick a network first" : baseUrl + ReceiverServer.ReceivePath, baseUrl is null ? null : baseUrl + ReceiverServer.ReceivePath),
               ("Both", "Authorization header", $"Bearer {token[..4]}…{token[^4..]}", $"Bearer {token}")]; // key masked on screen; Copy gets all of it
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        var muted = Program.LightTheme ? Color.FromArgb(105, 105, 105) : Color.FromArgb(150, 150, 150);
        var bold = new Font(Font, FontStyle.Bold);
        var mono = new Font("Consolas", Font.Size * 0.95f);
        table.Disposed += (_, _) => { bold.Dispose(); mono.Dispose(); };
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var name = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 14, 4) };
            name.Controls.Add(new Label { Text = row.Shortcut, Font = bold, AutoSize = true, Margin = Padding.Empty });
            name.Controls.Add(new Label { Text = row.Field, ForeColor = muted, AutoSize = true, Margin = Padding.Empty });
            var value = new Label { Text = row.Shown, Font = mono, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 10, 4) };
            var copy = Button("Copy");
            copy.Anchor = AnchorStyles.Right;
            copy.Visible = row.Copy is not null; // nothing to copy in the demo or before a network is picked
            copy.AccessibleName = $"Copy {row.Shortcut} {row.Field}";
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(row.Copy!); copy.Text = "Copied"; }
                catch (System.Runtime.InteropServices.ExternalException) { copy.Text = "Try again"; }
            };
            // Fixed cells: a hidden Copy button must not let the next row slide into its place.
            table.Controls.Add(name, 0, index);
            table.Controls.Add(value, 1, index);
            table.Controls.Add(copy, 2, index);
        }
        return table;
    }

    // A drawn circle (not a "●" glyph, which sits low in its line), centered on the status text.
    private sealed class StatusDot : Control
    {
        public StatusDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(14, 20);
            TabStop = false;
        }

        protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var diameter = LogicalToDeviceUnits(10);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(ForeColor);
            e.Graphics.FillEllipse(brush, (Width - diameter) / 2f, (Height - diameter) / 2f + 1, diameter, diameter);
        }
    }

    private static Color StartColor => Program.LightTheme ? Color.FromArgb(38, 140, 64) : Color.FromArgb(92, 196, 108);
    private static Color PauseColor => Program.LightTheme ? Color.FromArgb(196, 140, 18) : Color.FromArgb(232, 186, 64);
    private static Color StopColor => Program.LightTheme ? Color.FromArgb(196, 58, 52) : Color.FromArgb(230, 92, 86);

    private static Button IconButton(string name, Bitmap? image = null)
    {
        var button = Button("", iconOnly: true);
        button.AutoSize = false;
        button.MinimumSize = Size.Empty;
        button.Size = new Size(36, 36);
        button.Anchor = AnchorStyles.Right;
        button.AccessibleName = name;
        button.Margin = new Padding(4, 0, 0, 0);
        if (image is not null) SetIcon(button, image);
        return button;
    }

    private static void SetIcon(Button button, Bitmap image)
    {
        var old = button.Image;
        button.Image = image;
        old?.Dispose();
    }

    // Solid media shapes: p = play, z = pause, s = stop.
    private Bitmap CreateReceiverIcon(char kind, Color color)
    {
        var size = LogicalToDeviceUnits(22);
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        float s = size;
        if (kind == 'p') graphics.FillPolygon(brush, [new PointF(s * .24f, s * .12f), new PointF(s * .88f, s * .5f), new PointF(s * .24f, s * .88f)]);
        else if (kind == 'z') { graphics.FillRectangle(brush, s * .2f, s * .14f, s * .2f, s * .72f); graphics.FillRectangle(brush, s * .6f, s * .14f, s * .2f, s * .72f); }
        else
        {
            using var square = new System.Drawing.Drawing2D.GraphicsPath();
            square.AddRoundedRectangle(Rectangle.Round(new RectangleF(s * .16f, s * .16f, s * .68f, s * .68f)), new Size(size / 6, size / 6));
            graphics.FillPath(brush, square);
        }
        return bitmap;
    }

    private static Bitmap CreateCalendarIcon(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(TextColor);
        float s = size;
        using var page = new System.Drawing.Drawing2D.GraphicsPath();
        page.AddRoundedRectangle(Rectangle.Round(new RectangleF(s * .1f, s * .16f, s * .8f, s * .76f)), new Size(size / 5, size / 5));
        page.AddRectangle(new RectangleF(s * .2f, s * .42f, s * .6f, s * .4f)); // cut-out day area
        graphics.FillPath(brush, page);
        graphics.FillRectangle(brush, s * .26f, s * .06f, s * .12f, s * .2f);
        graphics.FillRectangle(brush, s * .62f, s * .06f, s * .12f, s * .2f);
        graphics.FillRectangle(brush, s * .28f, s * .5f, s * .14f, s * .12f);
        return bitmap;
    }

    private static Bitmap CreateHomeIcon(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float s = size;
        using var house = new System.Drawing.Drawing2D.GraphicsPath { FillMode = System.Drawing.Drawing2D.FillMode.Alternate };
        house.AddPolygon([new PointF(s * .5f, s * .08f), new PointF(s * .96f, s * .48f), new PointF(s * .84f, s * .48f), new PointF(s * .84f, s * .9f),
            new PointF(s * .16f, s * .9f), new PointF(s * .16f, s * .48f), new PointF(s * .04f, s * .48f)]);
        house.AddRectangle(new RectangleF(s * .41f, s * .6f, s * .18f, s * .3f)); // door cut-out
        using var brush = new SolidBrush(TextColor);
        graphics.FillPath(brush, house);
        return bitmap;
    }

    private static Bitmap CreateCartIcon(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float s = size;
        using var brush = new SolidBrush(TextColor);
        using var pen = new Pen(TextColor, s * .09f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        graphics.DrawLines(pen, [new PointF(s * .04f, s * .14f), new PointF(s * .2f, s * .14f), new PointF(s * .3f, s * .62f), new PointF(s * .82f, s * .62f)]);
        graphics.FillPolygon(brush, [new PointF(s * .22f, s * .24f), new PointF(s * .94f, s * .24f), new PointF(s * .84f, s * .54f), new PointF(s * .28f, s * .54f)]);
        graphics.FillEllipse(brush, s * .3f, s * .72f, s * .16f, s * .16f);
        graphics.FillEllipse(brush, s * .7f, s * .72f, s * .16f, s * .16f);
        return bitmap;
    }

    // Preview tile for one edge style on the Customize page.
    private sealed class EdgeTile : Control
    {
        private readonly string _style;
        private bool _selected;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

        public EdgeTile(string style)
        {
            _style = style;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(230, 170);
            Margin = new Padding(0, 0, 14, 14);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = EdgeArt.Label(style);
            AccessibleRole = AccessibleRole.RadioButton;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent?.BackColor ?? CalendarView.Page);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var label = Font.Height + LogicalToDeviceUnits(10);
            var frame = new Rectangle(1, 1, Width - 3, Height - label - 2);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddRoundedRectangle(frame, new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16)));
            using (var canvas = new SolidBrush(Canvas)) graphics.FillPath(canvas, path);
            var band = LogicalToDeviceUnits(14);
            var pad = LogicalToDeviceUnits(10);
            using (var inner = new SolidBrush(Surface)) graphics.FillRectangle(inner, frame.X + band + 4, frame.Y + pad, frame.Width - band * 2 - 8, frame.Height - pad - band - 6);
            graphics.SetClip(path);
            EdgeArt.Draw(graphics, Rectangle.Inflate(frame, -4, -4), new Padding(band, 0, band, band), _style);
            graphics.ResetClip();
            using var outline = new Pen(_selected ? TextColor : Program.LightTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(60, 60, 60), _selected ? 2 : 1);
            graphics.DrawPath(outline, path);
            TextRenderer.DrawText(graphics, EdgeArt.Label(_style), Font, new Rectangle(0, Height - label, Width, label), TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(frame, -3, -3));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    private static Bitmap CreateChevronIcon(int size, bool left)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float s = size;
        using var pen = new Pen(TextColor, s * .16f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        float near = left ? s * .62f : s * .38f, far = left ? s * .34f : s * .66f;
        graphics.DrawLines(pen, [new PointF(near, s * .18f), new PointF(far, s * .5f), new PointF(near, s * .82f)]);
        return bitmap;
    }

    private static Bitmap CreateRefreshIcon(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float s = size;
        using var pen = new Pen(TextColor, s * .14f);
        graphics.DrawArc(pen, s * .16f, s * .16f, s * .68f, s * .68f, -60, 300);
        using var brush = new SolidBrush(TextColor);
        graphics.FillPolygon(brush, [new PointF(s * .6f, s * .02f), new PointF(s * .98f, s * .2f), new PointF(s * .62f, s * .4f)]);
        return bitmap;
    }

    private static Bitmap CreateSettingsIcon(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shape = new System.Drawing.Drawing2D.GraphicsPath();
        var center = size / 2f;
        float[] radii = [.34f, .48f, .48f, .34f];
        var points = Enumerable.Range(0, 32).Select(index =>
        {
            var angle = (index - 1.5) * Math.PI / 16;
            var radius = size * radii[index % 4];
            return new PointF(center + radius * (float)Math.Cos(angle), center + radius * (float)Math.Sin(angle));
        }).ToArray();
        shape.AddPolygon(points);
        shape.AddEllipse(size * .29f, size * .29f, size * .42f, size * .42f);
        using var brush = new SolidBrush(TextColor);
        graphics.FillPath(brush, shape);
        return bitmap;
    }

    private bool PromptForDeveloperPassword(Form owner)
    {
        using var prompt = new Form
        {
            Text = "Unlock developer mode",
            ClientSize = new Size(420, 178), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Canvas, Font = Font
        };
        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var password = new TextBox { Width = 378, MaxLength = 256, UseSystemPasswordChar = true, AccessibleName = "Developer password" };
        StyleTextBox(password);
        fields.Controls.Add(new Label { Text = "Enter the developer password.", AutoSize = true });
        fields.Controls.Add(password);
        var remember = new CheckBox { Text = "Remember on this PC", AutoSize = true };
        fields.Controls.Add(remember);
        var action = SettingsButton("Unlock", primary: true);
        action.Click += (_, _) =>
        {
            if (password.Text != DeveloperModePassword)
            {
                MessageBox.Show(prompt, "The developer password is incorrect.", prompt.Text);
                password.SelectAll();
                password.Focus();
                return;
            }
            if (remember.Checked)
            {
                try { LocalFiles.Write(_rememberDeveloperPath, []); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(prompt, "Could not remember developer mode. Uncheck the option to unlock just for this session.", prompt.Text);
                    return;
                }
            }
            prompt.DialogResult = DialogResult.OK;
        };
        fields.Controls.Add(action);
        prompt.AcceptButton = action;
        prompt.Controls.Add(fields);
        return prompt.ShowDialog(owner) == DialogResult.OK;
    }

    private async Task CheckForUpdateAsync(bool interactive, Form? owner = null)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            var sourceFile = Path.Combine(AppContext.BaseDirectory, "update-source.txt");
            var source = File.Exists(sourceFile) ? File.ReadAllText(sourceFile).Trim() : null;
            if (!Uri.TryCreate(source, UriKind.Absolute, out var feed) || feed.Scheme != Uri.UriSchemeHttps)
            {
                if (interactive) MessageBox.Show(owner ?? this, "This build has no HTTPS update feed configured.", "Updates");
                return;
            }
            // A github.com repo address reads its public Releases; anything else is a plain HTTPS folder.
            // No token is ever embedded, so a GitHub feed only works once the repo is public.
            Velopack.Sources.IUpdateSource updates = feed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new Velopack.Sources.GithubSource(feed.AbsoluteUri.TrimEnd('/'), null, false, null)
                : new Velopack.Sources.SimpleWebSource(feed, null, 30);
            var manager = new Velopack.UpdateManager(updates, null, null);
            if (!manager.IsInstalled)
            {
                if (interactive) MessageBox.Show(owner ?? this, "Updates are available for installed builds.", "Updates");
                return;
            }
            var update = await manager.CheckForUpdatesAsync();
            if (_quitting || IsDisposed) return;
            if (update is null)
            {
                if (interactive) MessageBox.Show(owner ?? this, "EasyClipboard is up to date.", "Updates");
                return;
            }
            if (MessageBox.Show(owner ?? this, "An EasyClipboard update is available. Download it and restart now?",
                "Updates", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            await manager.DownloadUpdatesAsync(update);
            if (_quitting || IsDisposed) return;
            await StopAsync();
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception)
        {
            if (interactive && !IsDisposed) MessageBox.Show(owner ?? this, "The update could not be completed. Try again later.", "Updates");
        }
        finally { _checkingUpdates = false; }
    }

    private async Task StartAsync()
    {
        if (_demo)
        {
            _demoReceiving = true;
            _allowFetch.Checked = _fetchAllowed;
            ShowStoppedSetup();
            UpdateReceiverControls();
            return;
        }
        if (_starting || _stopRequests > 0 || _quitting || _server is not null) return;
        if (TailscaleMode)
        {
            RefreshInterfaces();
            if (_interfaces.SelectedItem is null) return;
        }
        _starting = true;
        _allowFetch.Checked = false;
        UpdateReceiverControls();
        await _receiverGate.WaitAsync();
        try
        {
            if (HomeMode)
            {
                _binding = _interfaces.SelectedItem as HotspotBinding
                    ?? throw new InvalidOperationException(TailscaleMode ? "Connect Tailscale on this laptop, then click Refresh networks. No active Tailscale IPv4 address was found."
                        : "Choose an active home Wi-Fi or Ethernet address. Click Refresh networks if needed.");
                if (!_binding.IsPresent()) throw new InvalidOperationException("That address is no longer active. Refresh networks and select the current address.");
            }
            else
            {
                await _hotspot.StartAsync();
                _binding = _hotspot.Binding!;
            }
            var server = new ReceiverServer(_binding, _identity, _transfers, allowHomeHttp: HttpTransport, clipboardExport: _clipboardExport);
            _server = server;
            server.PeerActivity += activity => Ui(() =>
            {
                if (!ReferenceEquals(_server, server)) return;
                var outcome = !activity.Finished ? "arrived" : activity.StatusCode is { } status
                    ? $"HTTP {status} in {activity.ElapsedMilliseconds / 1000.0:F1}s" : "interrupted";
                _connection.Text = $"Phone requests: {activity.RequestCount} ({activity.ActiveRequests} active). Last: {activity.Operation}, {outcome}, {activity.At:T}.";
            });
            await server.StartAsync();
            _allowFetch.Checked = _fetchAllowed && _stopRequests == 0 && !_quitting;
            _connection.Text = "Phone requests: none yet. Open the receiver page on your phone to test.";
            var connection = TailscaleMode ? "Keep both devices connected to your Tailscale network. Different Wi-Fi networks or phone cellular data can be used."
                : HomeMode ? "Keep the iPhone on the same house Wi-Fi / LAN as this laptop." : $"Join private Wi-Fi: {_identity.Ssid}";
            var url = HttpTransport ? $"http://{_binding.Address}:{ReceiverServer.SetupPort}{ReceiverServer.SendPath}"
                : $"https://{_binding.Address}:{ReceiverServer.TransferPort}{ReceiverServer.SendPath}";
            var trust = TailscaleMode ? "HTTP travels inside Tailscale's encrypted connection. No iPhone certificate profile is needed."
                : HomeHttp ? "No iPhone profile is needed. HTTP content and the pairing key are unencrypted."
                : $"Certificate install: http://{_binding.Address}:{ReceiverServer.SetupPort}/install.mobileconfig";
            var fetchUrl = url.Replace(ReceiverServer.SendPath, ReceiverServer.ReceivePath);
            _setup.Text = $"{connection}\r\nSend to PC URL: {url}\r\nGet from PC URL: {fetchUrl}\r\n{trust}\r\n\r\nOpen Pairing / setup for your key.";
            if (_developerUnlocked) _setup.AppendText($"\r\nFirewall interface ID: {_binding.InterfaceId}\r\nHistory: {_bank.Root}");
            _status.Text = "Receiving. Copy on iPhone, then run Send to Laptop.";
        }
        catch (Exception error)
        {
            await StopCoreAsync();
            _status.Text = $"Couldn't start: {error.Message} (0x{error.HResult:X8}).";
        }
        finally { _starting = false; _receiverGate.Release(); UpdateReceiverControls(); }
    }

    private async Task StopAsync()
    {
        if (_demo)
        {
            _demoReceiving = false;
            _allowFetch.Checked = _pause.Checked = false;
            ShowStoppedSetup();
            UpdateReceiverControls();
            return;
        }
        _allowFetch.Checked = false;
        _stopRequests++;
        UpdateReceiverControls();
        await _receiverGate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _stopRequests--; _receiverGate.Release(); UpdateReceiverControls(); }
    }

    private void UpdateReceiverControls()
    {
        var busy = _starting || _stopRequests > 0 || _quitting;
        var running = _server is not null || _demoReceiving;
        var paused = running && _pause.Checked;
        _start.Enabled = !busy && (running || _demo || !TailscaleMode || _interfaces.Items.Count > 0);
        _stop.Enabled = !busy && running;
        SetIcon(_start, CreateReceiverIcon(running && !paused ? 'z' : 'p', !_start.Enabled ? DisabledText : running && !paused ? PauseColor : StartColor));
        SetIcon(_stop, CreateReceiverIcon('s', _stop.Enabled ? StopColor : DisabledText));
        _start.AccessibleName = !running ? HomeMode ? "Start receiving" : "Start hotspot" : paused ? "Resume receiving" : "Pause receiving";
        _tips.SetToolTip(_start, _start.AccessibleName);
        _state.Text = _starting ? "Starting" : _stopRequests > 0 ? "Stopping" : paused ? "Paused" : running ? "Active" : "Stopped";
        _blink.Enabled = running && !paused && !busy;
        _dot.ForeColor = paused ? PauseColor : running ? StartColor : DisabledText;
        _mode.Enabled = _developerUnlocked && !busy && !running;
        _interfaces.Enabled = _refresh.Enabled = !busy && !running && HomeMode;
        _allowFetch.Enabled = !busy && running;
        _pause.Enabled = _trayPause.Enabled = !busy && running;
    }

    private async Task StopCoreAsync()
    {
        _allowFetch.Checked = false;
        try
        {
            if (_server is not null) await _server.DisposeAsync();
        }
        catch (Exception) { _status.Text = "Receiver didn't stop cleanly. Quit and check Windows networking."; }
        finally
        {
            _server = null;
            _pause.Checked = false;
            _binding = null;
            try { await _hotspot.StopAsync(); }
            catch (Exception) { _status.Text = "Hotspot didn't stop cleanly. Check Windows networking."; }
            _connection.Text = "Phone requests: off.";
            _status.Text = "Stopped. Your pairing is saved.";
            ShowStoppedSetup();
        }
    }

    private void ShowPairing()
    {
        using var dialog = new Form { Text = "Pair your iPhone", ClientSize = new Size(760, 460), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var contents = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = true };
        dialog.BackColor = Canvas;
        StyleTextBox(contents);
        if (_demo)
        {
            contents.Text = "Receiver URL: [hidden]\r\nPairing key: [hidden]\r\nLocal data: %LOCALAPPDATA%\\EasyClipboard";
            dialog.Controls.Add(contents);
            dialog.ShowDialog(this);
            return;
        }
        void Update()
        {
            var network = TailscaleMode ? "Connect your iPhone and laptop to the same Tailscale network. Use the laptop's Tailscale address in the Shortcut."
                : HomeMode ? "Connect your iPhone to the same house Wi-Fi / LAN as the laptop."
                : $"Join Wi-Fi: {_identity.Ssid}\r\nWi-Fi password: {_identity.WifiPassword}";
            var trust = TailscaleMode ? "Tailscale encrypts the connection between devices. The HTTP receiver binds only its Tailscale IP. No custom certificate profile is needed."
                : HomeHttp ? "Home HTTP mode needs no iPhone profile. Content and this key travel unencrypted on your network."
                : $"Root certificate SHA-256 (compare before trusting):\r\n{_identity.Fingerprint}\r\n\r\nRoot name: {_identity.Authority.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}\r\n\r\nInstalling this root grants trust beyond EasyClipboard. See docs/shortcut.md for verification and removal.";
            contents.Text = $"{network}\r\n\r\nShortcut Authorization header:\r\nBearer {_identity.Token}\r\n\r\n{trust}\r\n\r\nThe pairing key is a secret. Revoking it invalidates the existing Shortcut key. This prototype pairs one phone/key at a time.";
        }
        // iPhone setup code inside the guide panel, bottom left: QR beside the same four lines (the panel already shows the key).
        dialog.ClientSize = new Size(820, 600);
        var guide = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        // A plain row with a fixed height (set below): an auto-sizing docked table here sent layout into a loop.
        var setup = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Surface, Padding = new Padding(10, 6, 10, 12) };
        var qrSide = LogicalToDeviceUnits(162); // ~150 px code plus a slim 6 px white border
        var qr = new PictureBox { Size = new Size(qrSide, qrSide), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White, Margin = new Padding(0, 0, 16, 0), AccessibleName = "Setup code QR" };
        var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var setupTitle = new Label { Text = "iPhone setup code", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var setupHint = new Label { Text = "Scan with your Settings Shortcut, or copy the text.", AutoSize = true, ForeColor = DisabledText, Margin = new Padding(0, 0, 0, 8) };
        var mono = new Font("Consolas", Font.Size * 0.85f);
        dialog.Disposed += (_, _) => { mono.Dispose(); setupTitle.Font.Dispose(); qr.Image?.Dispose(); };
        var lines = new TextBox { Multiline = true, ReadOnly = true, WordWrap = false, Font = mono, Width = LogicalToDeviceUnits(520), Height = mono.Height * 4 + LogicalToDeviceUnits(8), AccessibleName = "Setup code text" };
        StyleTextBox(lines);
        var copyAll = Button("Copy all");
        copyAll.Margin = new Padding(0, 8, 0, 0);
        side.Controls.AddRange([setupTitle, setupHint, lines, copyAll]);
        setup.Controls.AddRange([qr, side]);
        setup.Height = Math.Max(qrSide, side.PreferredSize.Height) + setup.Padding.Vertical + 4;
        void ShowSetupCode()
        {
            var code = SetupCode();
            qr.Image?.Dispose();
            qr.Image = code is null ? null : SetupQr(code, qrSide - LogicalToDeviceUnits(12));
            qr.Visible = code is not null; // no blank white square before a network is picked
            lines.Text = code is null ? "Pick a network on the main window first." : code.ReplaceLineEndings("\r\n"); // TextBox needs CRLF to break lines
            copyAll.Enabled = code is not null;
            copyAll.Text = "Copy all";
        }
        copyAll.Click += (_, _) =>
        {
            if (SetupCode() is not { } code) return;
            try { Clipboard.SetText(code); copyAll.Text = "Copied"; }
            catch (System.Runtime.InteropServices.ExternalException) { copyAll.Text = "Try again"; }
        };
        Update();
        ShowSetupCode();
        var revoke = Button("Revoke key and create a new one");
        revoke.Dock = DockStyle.Bottom;
        revoke.Click += (_, _) => Guard(() =>
        {
            _identity.RotateToken();
            Update();
            ShowSetupCode(); // a new key means a new setup code; the old one stops working
        });
        guide.Controls.Add(contents);
        guide.Controls.Add(setup);
        dialog.Shown += (_, _) => { contents.Select(0, 0); copyAll.Focus(); }; // don't open with the whole guide highlighted
        dialog.Controls.Add(guide);
        dialog.Controls.Add(revoke);
        dialog.ShowDialog(this);
    }

    // The receiver's base URL for the selected (or running) network, or null before one is picked.
    private string? ReceiverBaseUrl()
    {
        var address = _binding?.Address ?? (_interfaces.SelectedItem as HotspotBinding)?.Address;
        return address is null ? null : HttpTransport
            ? $"http://{address}:{ReceiverServer.SetupPort}" : $"https://{address}:{ReceiverServer.TransferPort}";
    }

    // Header line (lets a Shortcut reject other codes), then SendURL, ReceiveURL, AuthKey in the order the Settings Shortcut reads them.
    private const string SetupCodeHeader = "EasyClipboard-1";
    private string? SetupCode() => ReceiverBaseUrl() is { } baseUrl
        ? $"{SetupCodeHeader}\n{baseUrl}{ReceiverServer.SendPath}\n{baseUrl}{ReceiverServer.ReceivePath}\n{_identity.Token}"
        : null;

    // Black on white, sized in whole pixels per module so it stays sharp. The caller adds a slim white border.
    private static Bitmap SetupQr(string code, int size)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCoder.QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix.Count - 8; // the matrix includes a 4-module quiet zone on each side, left out below
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(Math.Max(2, size / modules), drawQuietZones: false);
        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }

    private void RefreshHistory()
    {
        var entries = _demo ? [] : _bank.ListEntries();
        _calendar.SetEntries([.. entries]);
        if (_imagesOnly.Checked) entries.RemoveAll(entry => !entry.IsImage);
        if (_textOnly.Checked) entries.RemoveAll(entry => entry.IsImage);
        _refreshingHistory = true;
        try
        {
            _items.SetEntries(entries);
        }
        finally
        {
            _refreshingHistory = false;
        }
        Preview();
        if (_bank.UnreadableFiles > 0 || _bank.MaintenanceWarning) _status.Text = "Some history couldn't be read or cleaned up. Check folder permissions.";
    }

    private void Preview()
    {
        if (_refreshingHistory || _closed) return;
        var entry = Visible && WindowState != FormWindowState.Minimized ? _items.SelectedItem as HistoryEntry : null;
        if (entry is not null && _previewedId == entry.Id) return;
        _previewedId = null;
        _image.Image?.Dispose();
        _image.Image = null;
        _image.Visible = false;
        _text.Visible = true;
        _text.Text = _demo ? "Clipboard history and preview are hidden in demo mode." :
            "Select a saved entry. Closing the window keeps the app in the tray.";
        if (entry is null) return;
        try
        {
            var item = _bank.Find(entry.Id);
            if (item?.Png is { } png)
            {
                using var stream = new MemoryStream(png);
                using var image = Image.FromStream(stream);
                // Keep only a screen-sized copy. Copy Again reads the original PNG from disk.
                var scale = Math.Min(1.0, 1024.0 / Math.Max(image.Width, image.Height));
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                _image.Image = new Bitmap(image, width, height);
                _image.Visible = true;
                _text.Visible = false;
            }
            else if (item?.Text is { } text) _text.Text = text.ReplaceLineEndings("\r\n"); // iPhone text uses LF; the TextBox only breaks lines on CRLF

            _previewedId = entry.Id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or
            System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        { _text.Text = "Could not load the saved preview. Try selecting the item again."; }
    }

    private void ConfigureStorage(IWin32Window? owner = null)
    {
        using var dialog = new Form { Text = "Local history storage", ClientSize = new Size(530, 245), StartPosition = FormStartPosition.CenterParent, Font = Font };
        dialog.BackColor = Canvas;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), FlowDirection = FlowDirection.TopDown };
        var days = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = _bank.Preferences.RetentionDays };
        var count = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = _bank.Preferences.MaxItems };
        var mb = new NumericUpDown { Minimum = 32, Maximum = 4096, Value = _bank.Preferences.MaxMegabytes };
        foreach (var (name, input) in new[] { ("Delete after (days)", days), ("Maximum entries", count), ("Maximum disk MB", mb) })
        {
            var row = new FlowLayoutPanel { AutoSize = true };
            row.Controls.Add(new Label { Text = name, Width = 160 }); row.Controls.Add(input); panel.Controls.Add(row);
        }
        panel.Controls.Add(new Label { Text = _bank.Root, AutoSize = true, MaximumSize = new Size(480, 0) });
        var save = Button("Save limits");
        save.Click += (_, _) => Guard(() => { _bank.Configure(new((int)days.Value, (int)count.Value, (int)mb.Value)); RefreshHistory(); dialog.Close(); });
        panel.Controls.Add(save);
        dialog.Controls.Add(panel);
        dialog.ShowDialog(owner ?? this);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception) { _status.Text = "Couldn't update local storage. Check disk space and permissions."; }
    }
    private void RefreshInterfaces()
    {
        if (_demo)
        {
            _tailscaleStatus.Text = "Demo: network disabled";
            UpdateReceiverControls();
            return;
        }
        IReadOnlyList<HotspotBinding> bindings = _preview ? [] :
            TailscaleMode ? HotspotBinding.FindTailscaleInterfaces() : HotspotBinding.FindHomeInterfaces();
        if (!bindings.SequenceEqual(_interfaces.Items.Cast<HotspotBinding>()))
        {
            var selected = (_interfaces.SelectedItem as HotspotBinding)?.Address;
            _interfaces.Items.Clear();
            foreach (var binding in bindings) _interfaces.Items.Add(binding);
            var index = bindings.ToList().FindIndex(binding => binding.Address.Equals(selected));
            if (bindings.Count > 0) _interfaces.SelectedIndex = Math.Max(0, index);
        }
        _tailscaleStatus.Visible = TailscaleMode;
        if (TailscaleMode) ShowTailscaleStatus(bindings.Count > 0);
        UpdateReceiverControls();
    }

    private void ShowTailscaleStatus(bool ready)
    {
        if (ready)
        {
            _tailscaleStatus.Text = "Tailscale ready";
            return;
        }
        using var service = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tailscale");
        _tailscaleStatus.Text = service is null ? "Tailscale not installed" : "Tailscale not connected";
    }

    private void ShowStoppedSetup()
    {
        if (_demo)
        {
            _setup.Text = "Send to PC URL: [receiver address hidden]\r\nGet from PC URL: [receiver address hidden]\r\n\r\nLocal data: %LOCALAPPDATA%\\EasyClipboard";
            _status.Text = _demoReceiving ? "Receiving (demo only)." : "Stopped (demo only).";
            _connection.Text = "Phone requests: off in demo.";
            return;
        }
        _setup.Text = TailscaleMode
        ? "1. Connect Tailscale on this laptop and on your iPhone (or its supported Shadowrocket integration).\r\n2. Select the laptop's Tailscale address, then Start receiving.\r\n3. Use the displayed URL in both Shortcut POST actions; keep the existing pairing key and JSON bodies.\r\n\r\nThe Tailscale tunnel encrypts traffic. No certificate profile is needed. See docs/tailscale.md.\r\nClosing this window keeps receiving in the tray. Use Quit there to stop."
        : HomeMode
        ? $"1. Keep the iPhone and laptop on the same house Wi-Fi / LAN.\r\n2. Select the laptop's active address above, then Start receiving.\r\n3. Open Pairing / setup for the key, and follow docs/shortcut.md.\r\n\r\n{(HomeHttp ? "HTTP mode needs no iPhone profile. Content and pairing key are unencrypted." : "HTTPS mode requires the verified local certificate profile.")}\r\nClosing this window keeps receiving in the tray. Use Quit there to stop."
        : "1. Start the private hotspot.\r\n2. Open Pairing / setup for Wi-Fi credentials and certificate trust.\r\n3. Follow docs/shortcut.md.\r\n\r\nClosing this window keeps receiving in the tray. Use Quit there to stop.";
        _status.Text = TailscaleMode ? "Stopped. Pick your Tailscale address, then press ▶."
            : HomeMode ? "Stopped. Pick your network, then press ▶." : "Stopped. Press ▶ to start the hotspot.";
    }
    private static void StyleTextBox(TextBox box)
    {
        if (SystemInformation.HighContrast) return;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Surface;
        box.ForeColor = TextColor;
    }

    private sealed class RoundedPane : Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal string? Caption { get; init; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool SquareTopLeft { get; init; }

        public RoundedPane(params Control[] contents)
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(8);
            DoubleBuffered = true;
            ResizeRedraw = true;
            Controls.AddRange(contents);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using var brush = new SolidBrush(Caption is null ? Surface : AppearanceSurface);
            var bounds = ClientRectangle;
            if (Caption is not null)
            {
                bounds.Y = Font.Height / 2;
                bounds.Height -= bounds.Y + 1;
                bounds.Width--;
            }
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (SystemInformation.HighContrast)
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
            else
            {
                using var outline = new System.Drawing.Drawing2D.GraphicsPath();
                var diameter = Math.Min(20, Math.Min(bounds.Width, bounds.Height));
                outline.AddRoundedRectangle(bounds, new Size(diameter, diameter));
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, outline);
                if (SquareTopLeft) e.Graphics.FillRectangle(brush, bounds.X, bounds.Y, diameter, diameter);
                if (Caption is not null)
                {
                    using var border = new Pen(Program.LightTheme ? Color.FromArgb(184, 184, 184) : Color.FromArgb(77, 77, 77));
                    e.Graphics.DrawPath(border, outline);
                }
            }
            if (Caption is null) return;
            using var bold = new Font(Font, FontStyle.Bold);
            var size = TextRenderer.MeasureText(e.Graphics, Caption, bold);
            // Break only the border line under the caption, so no canvas-colored block shows inside the pane.
            using (var gap = new SolidBrush(Canvas))
                e.Graphics.FillRectangle(gap, 12, bounds.Y - 1, size.Width + 8, 2);
            TextRenderer.DrawText(e.Graphics, Caption, bold, new Point(16, 0), TextColor);
        }
    }

    private static Button Button(string text, bool receiverAction = false, bool iconOnly = false)
    {
        var button = new ReadableButton { Text = text, AutoSize = true, Margin = new Padding(0, 3, 9, 8), IconOnly = iconOnly };
        if (SystemInformation.HighContrast) return button;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        if (Program.LightTheme)
        {
            button.MinimumSize = new Size(100, 34);
            button.BackColor = receiverAction ? Color.FromArgb(207, 207, 207) : Surface;
            button.ForeColor = TextColor;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = receiverAction ? Color.FromArgb(195, 195, 195) : Color.FromArgb(232, 232, 232);
            button.FlatAppearance.MouseDownBackColor = receiverAction ? Color.FromArgb(185, 185, 185) : Color.FromArgb(222, 222, 222);
            return button; // light buttons paint their own rounded shape; a clip region would shave the border
        }
        RoundButtonCorners(button);
        var fill = receiverAction ? Color.FromArgb(65, 65, 65) : SelectionColor;
        button.BackColor = fill;
        button.ForeColor = TextColor;
        button.FlatAppearance.BorderColor = receiverAction ? Color.Silver : ControlPaint.Light(fill, 0.25f);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fill, 0.12f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(fill, 0.12f);
        return button;
    }

    private static Button SettingsButton(string text, bool primary = false)
    {
        var button = Button(text);
        button.MinimumSize = new Size(100, 34);
        if (SystemInformation.HighContrast || Program.LightTheme) return button;

        var fill = primary ? Color.FromArgb(63, 63, 63) : SelectionColor;
        button.BackColor = fill;
        button.ForeColor = primary ? Color.FromArgb(245, 245, 245) : TextColor;
        button.FlatAppearance.BorderColor = fill;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fill, 0.1f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(fill, 0.08f);
        return button;
    }

    private static void RoundButtonCorners(Control button)
    {
        void UpdateRegion()
        {
            if (button.Width < 10 || button.Height < 10) return;
            using var outline = new System.Drawing.Drawing2D.GraphicsPath();
            outline.AddRoundedRectangle(button.ClientRectangle, new Size(10, 10));
            button.Region = new Region(outline);
        }

        button.SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();
    }

    private static Color DisabledText => Program.LightTheme ? Color.FromArgb(102, 102, 102) : Color.FromArgb(155, 155, 155);
    private static Color DisabledBorder => Program.LightTheme ? Color.FromArgb(162, 162, 162) : Color.FromArgb(87, 87, 87);

    private sealed class ReadableButton : Button
    {
        // Icon-only buttons (the settings gear) show no box until hovered.
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool IconOnly { get; init; }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            if (Program.LightTheme || IconOnly)
            {
                PaintLightButton(e.Graphics);
                return;
            }

            if (Enabled)
                base.OnPaint(e);
            else
            {
                e.Graphics.Clear(Canvas);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, DisabledText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (Width < 10 || Height < 10) return;
            using var outline = new System.Drawing.Drawing2D.GraphicsPath();
            outline.AddRoundedRectangle(Rectangle.Inflate(ClientRectangle, -1, -1), new Size(8, 8));
            using var border = new Pen(Enabled ? FlatAppearance.BorderColor : DisabledBorder);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(border, outline);
        }

        private void PaintLightButton(Graphics graphics)
        {
            graphics.Clear(Parent?.BackColor ?? Canvas);
            var hovering = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            var fill = IconOnly && (!Enabled || !hovering) ? Parent?.BackColor ?? Canvas
                : !Enabled ? Color.FromArgb(235, 235, 235) : !hovering ? BackColor
                : Capture && MouseButtons == MouseButtons.Left ? FlatAppearance.MouseDownBackColor
                : FlatAppearance.MouseOverBackColor;
            using var shape = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = Math.Min(LogicalToDeviceUnits(10), Math.Min(Width, Height));
            if (diameter <= 0) return;
            shape.AddRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), new Size(diameter, diameter));
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(fill);
            graphics.FillPath(brush, shape);
            if (Enabled && !IconOnly)
            {
                using var border = new Pen(Color.FromArgb(190, 190, 190));
                graphics.DrawPath(border, shape);
            }

            if (Image is { } icon)
                graphics.DrawImageUnscaled(icon, (Width - icon.Width) / 2, (Height - icon.Height) / 2);
            else
                TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : DisabledText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues && Width > 10 && Height > 10)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, fill);
        }
    }

    private sealed class ReadableCheckBox : CheckBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled || SystemInformation.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            e.Graphics.Clear(BackColor);
            var state = Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedDisabled
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedDisabled;
            var glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
            var position = new Point(Padding.Left, (Height - glyph.Height) / 2);
            var box = new Rectangle(position, glyph);
            ControlPaint.DrawBorder(e.Graphics, box, DisabledBorder, ButtonBorderStyle.Solid);
            if (Checked)
                TextRenderer.DrawText(e.Graphics, "✓", Font, box, DisabledText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var textLeft = position.X + glyph.Width + 3;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(textLeft, 0, Width - textLeft, Height), DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class AppearanceRadio : RadioButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            var color = Enabled ? TextColor : DisabledText;
            var size = LogicalToDeviceUnits(13);
            var ring = new Rectangle(1, (Height - size) / 2, size, size);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(color);
            e.Graphics.DrawEllipse(pen, ring);
            if (Checked)
            {
                using var dot = new SolidBrush(color);
                var inset = LogicalToDeviceUnits(4);
                e.Graphics.FillEllipse(dot, Rectangle.Inflate(ring, -inset, -inset));
            }
            var text = Rectangle.FromLTRB(ring.Right + LogicalToDeviceUnits(4), 0, Width, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, text, color, BackColor);
        }
    }

    private void Ui(Action action)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
