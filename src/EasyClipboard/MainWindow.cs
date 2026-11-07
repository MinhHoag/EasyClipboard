using EasyClipboard.HotspotProbe;

namespace EasyClipboard;

internal sealed class MainWindow : Form
{
    private readonly Identity _identity;
    private readonly HistoryBank _bank;
    private readonly HotspotHost _hotspot;
    private readonly WindowsClipboard _clipboard;
    private readonly TransferService _transfers;
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
    private readonly string _modePath;
    private readonly NotifyIcon _tray;
    private readonly ListBox _items = new() { Dock = DockStyle.Fill, DisplayMember = "" };
    private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly PictureBox _image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = Color.WhiteSmoke };
    private readonly Label _status = new() { Text = "Stopped. Select your home network and start receiving.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _connection = new() { Text = "Phone requests: receiver stopped.", AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox _setup = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 145 };
    private readonly Button _start = Button("Start receiving");
    private readonly Button _stop = Button("Stop");
    private bool _quitting;
    private bool _closed;
    private bool _starting;
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private int _stopRequests;

    public MainWindow(string? dataRoot = null)
    {
        Text = "EasyClipboard — prototype";
        ClientSize = new Size(970, 750);
        MinimumSize = new Size(760, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        _preview = dataRoot is not null;
        _identity = new Identity(dataRoot ?? LocalFiles.DefaultRoot);
        _bank = new HistoryBank(dataRoot ?? LocalFiles.DefaultRoot);
        _modePath = Path.Combine(_bank.Root, "connection-mode.txt");
        _hotspot = new HotspotHost(false) { Ssid = _identity.Ssid, Password = _identity.WifiPassword };
        _clipboard = new WindowsClipboard(this);
        _transfers = new TransferService(_bank, _clipboard);
        _transfers.Changed += () => Ui(() => { _status.Text = "Received, saved locally, and copied. Ready for Ctrl+V."; RefreshHistory(); });
        _hotspot.StatusChanged += message => Ui(() => _status.Text = message);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var network = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _mode.Items.AddRange(["Home Wi-Fi / LAN — HTTP", "Home Wi-Fi / LAN — HTTPS", "Private hotspot — HTTPS", "Tailscale — encrypted tunnel"]);
        _mode.SelectedIndex = File.Exists(_modePath) && int.TryParse(File.ReadAllText(_modePath), out var savedMode) && savedMode is >= 0 and <= 3
            ? savedMode : !_preview && HotspotBinding.FindTailscaleInterfaces().Count > 0 ? 3 : 0;
        network.Controls.AddRange([_mode, _interfaces, _refresh]);
        layout.Controls.Add(network);
        _refresh.Click += (_, _) => RefreshInterfaces();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _interfaces.Enabled = _refresh.Enabled = HomeMode;
            _start.Text = HomeMode ? "Start receiving" : "Start hotspot";
            RefreshInterfaces();
            Guard(() => LocalFiles.Write(_modePath, System.Text.Encoding.UTF8.GetBytes(_mode.SelectedIndex.ToString())));
            ShowStoppedSetup();
        };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var pause = new CheckBox { Text = "Pause receiving", AutoSize = true, Padding = new Padding(6) };
        pause.CheckedChanged += (_, _) => _transfers.Paused = pause.Checked;
        var pairing = Button("Pairing / setup");
        pairing.Click += (_, _) => ShowPairing();
        actions.Controls.AddRange([_start, _stop, pause, pairing]);
        layout.Controls.Add(actions);
        layout.Controls.Add(_setup);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 350), SplitterDistance = 350 };
        split.Panel1.Controls.Add(_items);
        split.Panel2.Controls.Add(_text);
        split.Panel2.Controls.Add(_image);
        layout.Controls.Add(split);
        var historyActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var copy = Button("Copy Again");
        copy.Click += async (_, _) =>
        {
            if (_items.SelectedItem is not ClipItem item) return;
            try { await _clipboard.ApplyAsync(item, () => { }); _status.Text = "Copied. Ready for Ctrl+V."; }
            catch (Exception) { _status.Text = "Could not access the clipboard. Try Copy Again in a moment."; }
        };
        var delete = Button("Delete");
        delete.Click += (_, _) => Guard(() => { if (_items.SelectedItem is ClipItem item) _bank.Delete(item.Id); RefreshHistory(); });
        var clear = Button("Clear history");
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Delete all locally saved clipboard entries?", "Clear history", MessageBoxButtons.YesNo) == DialogResult.Yes)
                Guard(() => { _bank.Clear(); RefreshHistory(); });
        };
        var retention = Button("Storage limits");
        retention.Click += (_, _) => ConfigureStorage();
        historyActions.Controls.AddRange([copy, delete, clear, retention]);
        layout.Controls.Add(historyActions);
        layout.Controls.Add(_status);
        layout.Controls.Add(_connection);
        _items.SelectedIndexChanged += (_, _) => Preview();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _stop.Enabled = false;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open history", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        var paused = new ToolStripMenuItem("Pause receiving") { CheckOnClick = true };
        paused.CheckedChanged += (_, _) => pause.Checked = paused.Checked;
        pause.CheckedChanged += (_, _) => paused.Checked = pause.Checked;
        menu.Items.Add(paused);
        menu.Items.Add("Pairing / setup", null, (_, _) => { Show(); ShowPairing(); });
        menu.Items.Add("Quit", null, (_, _) => { _quitting = true; Close(); });
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "EasyClipboard", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += async (_, args) =>
        {
            if (_closed) return;
            args.Cancel = true;
            if (!_quitting) { Hide(); return; }
            Enabled = false;
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
    }

    internal void FinishPreview() { _quitting = true; Close(); }

    private async Task StartAsync()
    {
        if (_starting || _stopRequests > 0 || _quitting || _server is not null) return;
        _starting = true;
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
            var server = new ReceiverServer(_binding, _identity, _transfers, allowHomeHttp: HttpTransport);
            _server = server;
            server.PeerActivity += activity => Ui(() =>
            {
                if (!ReferenceEquals(_server, server)) return;
                var outcome = !activity.Finished ? "arrived" : activity.StatusCode is { } status ? $"HTTP {status}" : "interrupted";
                _connection.Text = $"Phone requests: {activity.RequestCount}; active: {activity.ActiveRequests}. Last: {activity.At:T} — {activity.Operation}, {outcome}.";
            });
            await server.StartAsync();
            _connection.Text = "Phone requests: none since Start. Open the receiver page on your phone to check.";
            Guard(() => LocalFiles.Write(_modePath, System.Text.Encoding.UTF8.GetBytes(_mode.SelectedIndex.ToString())));
            var connection = TailscaleMode ? "Keep both devices connected to your Tailscale network. Different Wi-Fi networks or phone cellular data can be used."
                : HomeMode ? "Keep the iPhone on the same house Wi-Fi / LAN as this laptop." : $"Join private Wi-Fi: {_identity.Ssid}";
            var url = HttpTransport ? $"http://{_binding.Address}:{ReceiverServer.SetupPort}/v1/transfers"
                : $"https://{_binding.Address}:{ReceiverServer.TransferPort}/v1/transfers";
            var trust = TailscaleMode ? "HTTP travels inside Tailscale's encrypted connection. No iPhone certificate profile is needed."
                : HomeHttp ? "No iPhone profile is needed. HTTP content and the pairing key are unencrypted."
                : $"Certificate install: http://{_binding.Address}:{ReceiverServer.SetupPort}/install.mobileconfig";
            _setup.Text = $"{connection}\r\nShortcut URL: {url}\r\n{trust}\r\nFirewall interface ID: {_binding.InterfaceId}\r\n\r\nOpen Pairing / setup for your key. History: {_bank.Root}";
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
        _mode.Enabled = !busy && !running;
        _interfaces.Enabled = _refresh.Enabled = !busy && !running && HomeMode;
    }

    private async Task StopCoreAsync()
    {
        try
        {
            if (_server is not null) await _server.DisposeAsync();
        }
        catch (Exception) { _status.Text = "Receiver cleanup failed. Quit and check Windows networking."; }
        finally
        {
            _server = null;
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
        var selected = (_items.SelectedItem as ClipItem)?.Id;
        _items.BeginUpdate();
        _items.Items.Clear();
        foreach (var item in _bank.List()) _items.Items.Add(item);
        _items.EndUpdate();
        _items.SelectedIndex = _items.Items.Cast<ClipItem>().ToList().FindIndex(x => x.Id == selected);
        if (_items.SelectedIndex < 0 && _items.Items.Count > 0) _items.SelectedIndex = 0;
        Preview();
        if (_bank.UnreadableFiles > 0 || _bank.MaintenanceWarning) _status.Text = "Some history could not be read or pruned. Check local storage and permissions.";
    }

    private void Preview()
    {
        _image.Image?.Dispose();
        _image.Image = null;
        var item = _items.SelectedItem as ClipItem;
        _text.Text = item?.Text ?? "Select a saved entry. Closing the window keeps the app in the tray.";
        _image.Visible = item?.Png is not null;
        _text.Visible = !_image.Visible;
        if (item?.Png is { } png)
        {
            using var stream = new MemoryStream(png);
            using var image = Image.FromStream(stream);
            _image.Image = new Bitmap(image);
        }
    }

    private void ConfigureStorage()
    {
        using var dialog = new Form { Text = "Local history storage", ClientSize = new Size(530, 245), StartPosition = FormStartPosition.CenterParent, Font = Font };
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
        dialog.ShowDialog(this);
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
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 3, 9, 8) };
    private void Ui(Action action)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
