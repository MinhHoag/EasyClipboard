using EasyClipboard.HotspotProbe;

namespace EasyClipboard;

internal sealed class MainWindow : Form
{
    private static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(29, 29, 29);
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.Black;
    internal static readonly Color SelectionColor = Color.FromArgb(48, 48, 48);
    private readonly Identity _identity;
    private readonly HistoryBank _bank;
    private readonly HotspotHost _hotspot;
    private readonly WindowsClipboard _clipboard;
    private readonly TransferService _transfers;
    private readonly ClipboardExport _clipboardExport;
    private readonly CheckBox _allowFetch = new ReadableCheckBox { Text = "Allow phone to fetch PC clipboard", AutoSize = true, Padding = new Padding(6), Enabled = false };
    private ReceiverServer? _server;
    private HotspotBinding? _binding;
    private readonly bool _preview;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _interfaces = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390 };
    private readonly Button _refresh = Button("Refresh networks");
    private bool HomeMode => _mode.SelectedIndex != 2;
    private bool HomeHttp => _mode.SelectedIndex == 0;
    private bool TailscaleMode => _mode.SelectedIndex == 3;
    private bool HttpTransport => HomeHttp || TailscaleMode;
    private readonly string _developerPasswordPath;
    private bool _developerUnlocked;
    private bool _checkingUpdates;
    private readonly NotifyIcon _tray;
    private readonly HistoryList _items = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _imagesOnly = new() { Text = "Images", AutoSize = true };
    private readonly RadioButton _textOnly = new() { Text = "Text", AutoSize = true };
    private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly PictureBox _image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = Surface };
    private readonly Label _status = new() { Text = "Stopped. Select your home network and start receiving.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _connection = new() { Text = "Phone requests: receiver stopped.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox _setup = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 145 };
    private readonly Button _start = Button("Start receiving", receiverAction: true);
    private readonly Button _stop = Button("Stop", receiverAction: true);
    private readonly CheckBox _pause = new ReadableCheckBox { Text = "Pause receiving", AutoSize = true, Padding = new Padding(6), Enabled = false };
    private readonly ToolStripMenuItem _trayPause = new("Pause receiving") { CheckOnClick = true, Enabled = false };
    private bool _quitting;
    private bool _closed;
    private bool _starting;
    private bool _refreshingHistory;
    private Guid? _previewedId;
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private int _stopRequests;

    public MainWindow(string? dataRoot = null, bool startReceiving = false)
    {
        Text = "EasyClipboard — prototype";
        ClientSize = new Size(970, 750);
        MinimumSize = new Size(760, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Canvas;
        Icon = Icon.ExtractIcon(Application.ExecutablePath, 0, 48) ?? SystemIcons.Application;
        var brandImage = Icon.ToBitmap();
        Disposed += (_, _) => brandImage.Dispose();
        StyleTextBox(_setup);
        StyleTextBox(_text);
        foreach (var combo in new[] { _mode, _interfaces })
        {
            if (SystemInformation.HighContrast) continue;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Surface;
            combo.ForeColor = Color.White;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var selected = (e.State & DrawItemState.Selected) != 0;
                using var background = new SolidBrush(selected ? SelectionColor : Surface);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), e.Font,
                    Rectangle.Inflate(e.Bounds, -2, 0), combo.Enabled ? Color.White : DisabledText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
        }
        _preview = dataRoot is not null;
        _identity = new Identity(dataRoot ?? LocalFiles.DefaultRoot);
        _bank = new HistoryBank(dataRoot ?? LocalFiles.DefaultRoot);
        _developerPasswordPath = Path.Combine(_bank.Root, "developer-password.dat");
        _hotspot = new HotspotHost(false) { Ssid = _identity.Ssid, Password = _identity.WifiPassword };
        _clipboard = new WindowsClipboard(this);
        _transfers = new TransferService(_bank, _clipboard);
        _clipboardExport = new ClipboardExport(_clipboard);
        _transfers.Changed += () => Ui(() => { _status.Text = "Received, saved locally, and copied. Ready for Ctrl+V."; RefreshHistory(); });
        _hotspot.StatusChanged += message => Ui(() => _status.Text = message);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        header.Controls.Add(new PictureBox { Image = brandImage, Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = Padding.Empty });
        header.Controls.Add(new Label { Text = "EasyClipboard", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(6, 15, 12, 0) });
        var settings = Button("⚙");
        settings.AccessibleName = "Settings";
        settings.Margin = new Padding(0, 11, 0, 0);
        settings.Click += (_, _) => ShowSettings();
        header.Controls.Add(settings);
        layout.Controls.Add(header);
        var network = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _mode.Items.AddRange(["Home Wi-Fi / LAN — HTTP", "Home Wi-Fi / LAN — HTTPS", "Private hotspot — HTTPS", "Tailscale — encrypted tunnel"]);
        _mode.SelectedIndex = 3;
        _mode.Visible = false;
        network.Controls.AddRange([_mode, _interfaces, _refresh]);
        layout.Controls.Add(network);
        _refresh.Click += (_, _) => RefreshInterfaces();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _interfaces.Enabled = _refresh.Enabled = HomeMode;
            _start.Text = HomeMode ? "Start receiving" : "Start hotspot";
            RefreshInterfaces();
            ShowStoppedSetup();
        };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _pause.CheckedChanged += (_, _) => _transfers.Paused = _pause.Checked;
        var pairing = Button("Pairing / setup");
        pairing.Click += (_, _) => ShowPairing();
        _allowFetch.CheckedChanged += (_, _) => _clipboardExport.Enabled = _allowFetch.Checked;
        actions.Controls.AddRange([_start, _stop, _pause, pairing, _allowFetch]);
        layout.Controls.Add(actions);
        layout.Controls.Add(new RoundedPane(_setup));
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
                filter.ForeColor = Color.White;
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
        layout.Controls.Add(split);
        var historyActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var copy = Button("Copy Again");
        copy.Click += async (_, _) =>
        {
            if (_items.SelectedItem is not HistoryEntry entry) return;
            try
            {
                var item = _bank.Find(entry.Id);
                if (item is null) { RefreshHistory(); return; }
                await _clipboard.ApplyAsync(item, () => { });
                _status.Text = "Copied. Ready for Ctrl+V.";
            }
            catch (Exception) { _status.Text = "Could not load or copy the saved item. Try Copy Again in a moment."; }
        };
        var delete = Button("Delete");
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
            { _status.Text = "Could not finish removing duplicates. Check local history permissions."; }
            finally
            {
                if (!IsDisposed)
                {
                    RefreshHistory();
                    duplicates.Enabled = true;
                }
            }
        };
        historyActions.Controls.AddRange([copy, delete, duplicates, clear]);
        layout.Controls.Add(historyActions);
        layout.Controls.Add(_status);
        layout.Controls.Add(_connection);
        _items.SelectedIndexChanged += (_, _) => Preview();
        VisibleChanged += (_, _) => Preview();
        Resize += (_, _) => Preview();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _stop.Enabled = false;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open history", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
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
            { _status.Text = "Could not check temporary Taildrop files. Check local storage and permissions."; }
        }
        taildropTimer.Tick += (_, _) =>
        {
            if (!_quitting && importTask is not { IsCompleted: false })
                importTask = ImportTaildropAsync();
        };
        if (!_preview) taildropTimer.Start();
        menu.Items.Add("Pairing / setup", null, (_, _) => { Show(); ShowPairing(); });
        menu.Items.Add("Quit", null, (_, _) => { _quitting = true; Close(); });
        _tray = new NotifyIcon { Icon = Icon, Text = "EasyClipboard", ContextMenuStrip = menu, Visible = !_preview };
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += async (_, args) =>
        {
            if (_closed) return;
            args.Cancel = true;
            if (!_quitting) { Hide(); return; }
            Enabled = false;
            taildropTimer.Dispose();
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
        if (startReceiving) Shown += async (_, _) => await StartAsync();
        if (!_preview) Shown += async (_, _) => await CheckForUpdateAsync(interactive: false);
    }

    internal void FinishPreview() { _quitting = true; Close(); }

    private void ShowSettings()
    {
        using var dialog = new Form
        {
            Text = "EasyClipboard settings", ClientSize = new Size(400, 205), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Canvas, Font = Font
        };
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var storage = Button("Storage limits");
        storage.Click += (_, _) => ConfigureStorage(dialog);
        var updates = Button("Check for updates");
        updates.Click += async (_, _) => await CheckForUpdateAsync(interactive: true, dialog);
        var developer = Button(_developerUnlocked ? "Lock developer mode" : "Unlock developer mode");
        developer.Enabled = _server is null && !_starting && _stopRequests == 0;
        developer.Click += (_, _) =>
        {
            if (!_developerUnlocked && !PromptForDeveloperPassword(dialog)) return;
            _developerUnlocked = !_developerUnlocked;
            _mode.SelectedIndex = 3;
            _mode.Visible = _developerUnlocked;
            UpdateReceiverControls();
            dialog.Close();
        };
        options.Controls.AddRange([storage, updates, developer]);
        dialog.Controls.Add(options);
        dialog.ShowDialog(this);
    }

    private bool PromptForDeveloperPassword(Form owner)
    {
        var creating = !File.Exists(_developerPasswordPath);
        using var prompt = new Form
        {
            Text = creating ? "Set a developer password" : "Unlock developer mode",
            ClientSize = new Size(420, creating ? 192 : 146), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Canvas, Font = Font
        };
        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var password = new TextBox { Width = 378, MaxLength = 256, UseSystemPasswordChar = true, AccessibleName = "Developer password" };
        var confirm = new TextBox { Width = 378, MaxLength = 256, UseSystemPasswordChar = true, AccessibleName = "Confirm developer password" };
        StyleTextBox(password);
        StyleTextBox(confirm);
        fields.Controls.Add(new Label { Text = creating ? "Choose a local password of at least 12 characters." : "Enter the developer password.", AutoSize = true });
        fields.Controls.Add(password);
        if (creating)
        {
            fields.Controls.Add(new Label { Text = "Confirm password", AutoSize = true });
            fields.Controls.Add(confirm);
        }
        var action = Button(creating ? "Set password and unlock" : "Unlock");
        action.Click += (_, _) =>
        {
            try
            {
                if (creating)
                {
                    if (password.Text.Length < 12 || password.Text != confirm.Text)
                    {
                        MessageBox.Show(prompt, "Enter matching passwords of at least 12 characters.", prompt.Text);
                        return;
                    }
                    LocalFiles.SaveSecret(_developerPasswordPath, DeveloperPassword.Create(password.Text));
                }
                else if (!LocalFiles.LoadSecret<DeveloperPassword>(_developerPasswordPath).Matches(password.Text))
                {
                    MessageBox.Show(prompt, "The developer password is incorrect.", prompt.Text);
                    return;
                }
                prompt.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
                MessageBox.Show(prompt, "The stored developer password could not be accessed.", prompt.Text);
            }
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
            var manager = new Velopack.UpdateManager(feed.AbsoluteUri);
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
        if (_starting || _stopRequests > 0 || _quitting || _server is not null) return;
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
                _connection.Text = $"Phone requests: {activity.RequestCount}; active: {activity.ActiveRequests}. Last: {activity.At:T} — {activity.Operation}, {outcome}.";
            });
            await server.StartAsync();
            _allowFetch.Checked = _stopRequests == 0 && !_quitting;
            _connection.Text = "Phone requests: none since Start. Open the receiver page on your phone to check.";
            var connection = TailscaleMode ? "Keep both devices connected to your Tailscale network. Different Wi-Fi networks or phone cellular data can be used."
                : HomeMode ? "Keep the iPhone on the same house Wi-Fi / LAN as this laptop." : $"Join private Wi-Fi: {_identity.Ssid}";
            var url = HttpTransport ? $"http://{_binding.Address}:{ReceiverServer.SetupPort}/v1/transfers"
                : $"https://{_binding.Address}:{ReceiverServer.TransferPort}/v1/transfers";
            var trust = TailscaleMode ? "HTTP travels inside Tailscale's encrypted connection. No iPhone certificate profile is needed."
                : HomeHttp ? "No iPhone profile is needed. HTTP content and the pairing key are unencrypted."
                : $"Certificate install: http://{_binding.Address}:{ReceiverServer.SetupPort}/install.mobileconfig";
            var fetchUrl = url.Replace("/v1/transfers", "/v1/clipboard");
            _setup.Text = $"{connection}\r\nSend to PC URL: {url}\r\nGet from PC URL: {fetchUrl}\r\n{trust}\r\n\r\nOpen Pairing / setup for your key.";
            if (_developerUnlocked) _setup.AppendText($"\r\nFirewall interface ID: {_binding.InterfaceId}\r\nHistory: {_bank.Root}");
            _status.Text = "Ready. Pair your iPhone, then invoke Send to Laptop after copying an item.";
        }
        catch (Exception error)
        {
            await StopCoreAsync();
            _status.Text = $"Could not start: {error.Message} (0x{error.HResult:X8}).";
        }
        finally { _starting = false; _receiverGate.Release(); UpdateReceiverControls(); }
    }

    private async Task StopAsync()
    {
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
        var running = _server is not null;
        _start.Enabled = !busy && !running;
        _stop.Enabled = !busy && running;
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
        catch (Exception) { _status.Text = "Receiver cleanup failed. Quit and check Windows networking."; }
        finally
        {
            _server = null;
            _pause.Checked = false;
            _binding = null;
            try { await _hotspot.StopAsync(); }
            catch (Exception) { _status.Text = "Hotspot cleanup failed. Check Windows networking."; }
            _connection.Text = "Phone requests: receiver stopped.";
            _status.Text = "Stopped. Your pairing credentials are saved for next time.";
            ShowStoppedSetup();
        }
    }

    private void ShowPairing()
    {
        using var dialog = new Form { Text = "Pair your iPhone", ClientSize = new Size(760, 460), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var contents = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = true };
        dialog.BackColor = Canvas;
        StyleTextBox(contents);
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
        Update();
        var revoke = Button("Revoke key and create a new one");
        revoke.Dock = DockStyle.Bottom;
        revoke.Click += (_, _) => Guard(() => { _identity.RotateToken(); Update(); });
        dialog.Controls.Add(contents);
        dialog.Controls.Add(revoke);
        dialog.ShowDialog(this);
    }

    private void RefreshHistory()
    {
        var entries = _bank.ListEntries();
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
        if (_bank.UnreadableFiles > 0 || _bank.MaintenanceWarning) _status.Text = "Some history could not be read or pruned. Check local storage and permissions.";
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
        _text.Text = "Select a saved entry. Closing the window keeps the app in the tray.";
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
            else if (item?.Text is { } text) _text.Text = text;
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
        foreach (var (name, input) in new[] { ("Retention days", days), ("Maximum entries", count), ("Maximum disk MB", mb) })
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
        catch (Exception) { _status.Text = "Could not update local storage. Check disk space and folder permissions."; }
    }
    private void RefreshInterfaces()
    {
        var selected = (_interfaces.SelectedItem as HotspotBinding)?.Address;
        _interfaces.Items.Clear();
        if (!_preview) foreach (var binding in TailscaleMode ? HotspotBinding.FindTailscaleInterfaces() : HotspotBinding.FindHomeInterfaces()) _interfaces.Items.Add(binding);
        _interfaces.SelectedIndex = _interfaces.Items.Cast<HotspotBinding>().ToList().FindIndex(x => x.Address.Equals(selected));
        if (_interfaces.SelectedIndex < 0 && _interfaces.Items.Count > 0) _interfaces.SelectedIndex = 0;
    }

    private void ShowStoppedSetup()
    {
        _setup.Text = TailscaleMode
        ? "1. Connect Tailscale on this laptop and on your iPhone (or its supported Shadowrocket integration).\r\n2. Select the laptop's Tailscale address, then Start receiving.\r\n3. Use the displayed URL in both Shortcut POST actions; keep the existing pairing key and JSON bodies.\r\n\r\nThe Tailscale tunnel encrypts traffic. No certificate profile is needed. See docs/tailscale.md.\r\nClosing this window keeps receiving in the tray. Use Quit there to stop."
        : HomeMode
        ? $"1. Keep the iPhone and laptop on the same house Wi-Fi / LAN.\r\n2. Select the laptop's active address above, then Start receiving.\r\n3. Open Pairing / setup for the key, and follow docs/shortcut.md.\r\n\r\n{(HomeHttp ? "HTTP mode needs no iPhone profile. Content and pairing key are unencrypted." : "HTTPS mode requires the verified local certificate profile.")}\r\nClosing this window keeps receiving in the tray. Use Quit there to stop."
        : "1. Start the private hotspot.\r\n2. Open Pairing / setup for Wi-Fi credentials and certificate trust.\r\n3. Follow docs/shortcut.md.\r\n\r\nClosing this window keeps receiving in the tray. Use Quit there to stop.";
        _status.Text = TailscaleMode ? "Stopped. Select your Tailscale address and start receiving."
            : HomeMode ? "Stopped. Select your home network and start receiving." : "Stopped. Start the private hotspot to receive.";
    }
    private static void StyleTextBox(TextBox box)
    {
        if (SystemInformation.HighContrast) return;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Surface;
        box.ForeColor = Color.White;
    }

    private sealed class RoundedPane : Panel
    {
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
            using var brush = new SolidBrush(Surface);
            if (SystemInformation.HighContrast)
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
                return;
            }

            using var outline = new System.Drawing.Drawing2D.GraphicsPath();
            var diameter = Math.Min(2 * Padding.Left + 4, Math.Min(ClientSize.Width, ClientSize.Height));
            outline.AddRoundedRectangle(ClientRectangle, new Size(diameter, diameter));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, outline);
        }
    }

    private static Button Button(string text, bool receiverAction = false)
    {
        var button = new ReadableButton { Text = text, AutoSize = true, Margin = new Padding(0, 3, 9, 8) };
        if (SystemInformation.HighContrast) return button;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        RoundButtonCorners(button);
        var fill = receiverAction ? Color.FromArgb(65, 65, 65) : SelectionColor;
        button.BackColor = fill;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = receiverAction ? Color.Silver : ControlPaint.Light(fill, 0.25f);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fill, 0.12f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(fill, 0.12f);
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

    private static readonly Color DisabledText = Color.FromArgb(155, 155, 155);
    private static readonly Color DisabledBorder = Color.FromArgb(87, 87, 87);

    private sealed class ReadableButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast)
            {
                base.OnPaint(e);
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
    private void Ui(Action action)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
