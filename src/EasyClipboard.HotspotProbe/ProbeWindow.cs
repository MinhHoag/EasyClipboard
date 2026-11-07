namespace EasyClipboard.HotspotProbe;

internal sealed class ProbeWindow : Form
{
    private readonly HotspotHost _host = new();
    private readonly Button _start = new() { Text = "Start private hotspot", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly TextBox _password;
    private readonly TextBox _url;
    private readonly TextBox _details;
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
    private bool _closed;
    private bool _closing;

    public ProbeWindow()
    {
        Text = "EasyClipboard — private hotspot test";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(750, 570);
        MinimumSize = new Size(650, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 12 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Test your private connection", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "This prototype checks reachability only. It does not read or send your clipboard.\nThe network provides no internet forwarding. Close this window to stop it.", AutoSize = true, Margin = new Padding(0, 10, 0, 14) });
        layout.Controls.Add(new Label { Text = "1. Start the hotspot, then select this network in iPhone Settings → Wi-Fi:", AutoSize = true });
        layout.Controls.Add(Field(_host.Ssid));
        layout.Controls.Add(new Label { Text = "Wi-Fi password (generated for this session):", AutoSize = true });
        _password = Field(_host.Password);
        _password.UseSystemPasswordChar = true;
        layout.Controls.Add(_password);
        var reveal = new CheckBox { Text = "Show password", AutoSize = true };
        reveal.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !reveal.Checked;
        layout.Controls.Add(reveal);
        layout.Controls.Add(new Label { Text = "2. Open this URL in Safari on the iPhone:", AutoSize = true, Margin = new Padding(0, 12, 0, 3) });
        _url = Field("Available after the hotspot starts");
        layout.Controls.Add(_url);
        _details = Field("If Safari cannot connect, see docs/hotspot-test.md for the narrow firewall rule.");
        _details.Multiline = true;
        _details.Height = 80;
        layout.Controls.Add(_details);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 10) };
        actions.Controls.AddRange([_start, _stop]);
        layout.Controls.Add(actions);
        layout.Controls.Add(_status);
        SetStatus("Stopped. Windows Mobile Hotspot and wireless display must be off before starting.");

        _start.Click += async (_, _) =>
        {
            _start.Enabled = false;
            _stop.Enabled = true;
            SetStatus("Starting the private access point…");
            try
            {
                await _host.StartAsync();
                if (_host.Binding is { } binding)
                {
                    _url.Text = $"http://{binding.Address}:{ProbeServer.DefaultPort}/v1/probe";
                    _details.Text = $"Safari should show probeId: {_host.ProbeId}\r\nInterface: {binding.InterfaceName}\r\nInterface ID: {binding.InterfaceId}\r\nHTTP carries only this public connection check. Real clipboard transfers will require HTTPS and pairing.";
                }
            }
            catch (OperationCanceledException) { SetStatus("Stopped."); _start.Enabled = true; _stop.Enabled = false; }
            catch (Exception error)
            {
                SetStatus($"Could not start: {error.Message} (0x{error.HResult:X8})");
                _start.Enabled = true;
                _stop.Enabled = false;
            }
        };
        _stop.Click += async (_, _) =>
        {
            _stop.Enabled = false;
            try
            {
                await _host.StopAsync();
                _url.Text = "Available after the hotspot starts";
                SetStatus("Stopped. The private access point and connection test are closed.");
            }
            catch (Exception error) { SetStatus($"Stop failed (0x{error.HResult:X8}). Close the app and check Windows networking."); }
            finally { _start.Enabled = true; }
        };
        _host.StatusChanged += message => OnUi(() => SetStatus(message));
        _host.PeerReached += () => OnUi(() => SetStatus("A device reached this session's test URL over the private network. Confirm that Safari shows the matching probeId above."));
        FormClosing += async (_, e) =>
        {
            if (_closed) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            Enabled = false;
            try { await _host.DisposeAsync(); }
            finally { _closed = true; Close(); }
        };
    }

    private static TextBox Field(string text) => new() { Text = text, ReadOnly = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 8) };
    private void SetStatus(string text) => _status.Text = text;
    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* Window closed while a callback was queued. */ }
    }
}
