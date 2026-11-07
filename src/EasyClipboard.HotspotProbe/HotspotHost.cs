using System.Security.Cryptography;
using Windows.Devices.WiFiDirect;

namespace EasyClipboard.HotspotProbe;

public sealed class HotspotHost : IAsyncDisposable
{
    private readonly bool _serveProbe;
    public HotspotHost(bool serveProbe = true) => _serveProbe = serveProbe;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _peerGate = new();
    private readonly List<WiFiDirectDevice> _peers = [];
    private WiFiDirectAdvertisementPublisher? _publisher;
    private WiFiDirectConnectionListener? _listener;
    private CancellationTokenSource? _session;
    private ProbeServer? _server;
    private TaskCompletionSource? _started;
    private bool _stopping;

    // Generated per run, shown only in the local window, never written to logs or configuration.
    public string Ssid { get; init; } = $"EasyClipboard-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
    public string Password { get; init; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    public HotspotBinding? Binding { get; private set; }
    public string? ProbeId => _server?.ProbeId;
    public event Action<string>? StatusChanged;
    public event Action? PeerReached;

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_publisher is not null) throw new InvalidOperationException("The hotspot is already running.");
            if (HotspotBinding.FindActive().Count != 0)
                throw new InvalidOperationException("Another Wi-Fi Direct interface is active. Close Mobile Hotspot or wireless display first.");

            _session = new CancellationTokenSource();
            var token = _session.Token;
            _stopping = false;
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _listener = new WiFiDirectConnectionListener();
            _listener.ConnectionRequested += OnConnectionRequested;
            _publisher = new WiFiDirectAdvertisementPublisher();
            _publisher.StatusChanged += OnPublisherChanged;
            var advertisement = _publisher.Advertisement;
            advertisement.IsAutonomousGroupOwnerEnabled = true;
            advertisement.LegacySettings.IsEnabled = true;
            advertisement.LegacySettings.Ssid = Ssid;
            advertisement.LegacySettings.Passphrase.Password = Password;
            _publisher.Start();
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            StatusChanged?.Invoke("Private access point started. Waiting for its local address…");

            // Discover the real address; do not assume 192.168.137.1 or change static IP settings.
            for (var attempt = 0; attempt < 60; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var candidates = HotspotBinding.FindActive();
                if (candidates.Count > 1)
                    throw new InvalidOperationException("Multiple Wi-Fi Direct addresses were found. Stop other wireless display/hotspot sessions and try again.");
                if (candidates.Count == 1)
                {
                    Binding = candidates[0];
                    break;
                }
                await Task.Delay(250, token);
            }
            if (Binding is null) throw new TimeoutException("The private access point did not receive an IPv4 address. Check Wi-Fi radio and driver support.");

            if (_serveProbe)
            {
                _server = new ProbeServer(Binding);
                _server.PeerReached += () => PeerReached?.Invoke();
                await _server.StartAsync(token);
            }
            StatusChanged?.Invoke("Ready. Join this Wi-Fi from your iPhone, then open the test URL in Safari.");
        }
        catch
        {
            await CleanupAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    private void OnPublisherChanged(WiFiDirectAdvertisementPublisher sender,
        WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        if (args.Status == WiFiDirectAdvertisementPublisherStatus.Started) _started?.TrySetResult();
        if (args.Status is WiFiDirectAdvertisementPublisherStatus.Aborted or WiFiDirectAdvertisementPublisherStatus.Stopped && !_stopping)
        {
            var message = $"Private hotspot stopped ({args.Error}). Check the Wi-Fi radio, driver, and Windows Mobile Hotspot conflicts.";
            _started?.TrySetException(new InvalidOperationException(message));
            _session?.Cancel();
            StatusChanged?.Invoke(message);
            // Close the HTTP listener too if the radio is lost after startup.
            _ = StopAfterAbortAsync();
        }
    }

    private async Task StopAfterAbortAsync()
    {
        try { await StopAsync(); }
        catch { StatusChanged?.Invoke("Hotspot cleanup failed. Close this window and check Windows networking settings."); }
    }

    private async void OnConnectionRequested(WiFiDirectConnectionListener sender,
        WiFiDirectConnectionRequestedEventArgs args)
    {
        // Capture the session before awaiting so a late result cannot become part of a later session.
        var session = _session;
        try
        {
            using var request = args.GetConnectionRequest();
            var peer = await WiFiDirectDevice.FromIdAsync(request.DeviceInformation.Id);
            if (peer is null)
            {
                StatusChanged?.Invoke("A device could not join. Recheck the Wi-Fi password and try again.");
                return;
            }
            lock (_peerGate)
            {
                if (session is null || session.IsCancellationRequested || !ReferenceEquals(session, _session))
                {
                    peer.Dispose();
                    return;
                }
                // Keep connections alive without retaining device identifiers or names in logs.
                _peers.Add(peer);
            }
            StatusChanged?.Invoke("A device joined the private network. Open the test URL on that device to verify reachability.");
        }
        catch (Exception error)
        {
            if (session is not null && !session.IsCancellationRequested)
                StatusChanged?.Invoke($"Device connection failed (0x{error.HResult:X8}). Rejoin the private Wi-Fi network.");
        }
    }

    public async Task StopAsync()
    {
        _session?.Cancel();
        await _gate.WaitAsync();
        try { await CleanupAsync(); }
        finally { _gate.Release(); }
    }

    private async Task CleanupAsync()
    {
        _stopping = true;
        _session?.Cancel();
        try
        {
            if (_server is not null) await _server.DisposeAsync();
        }
        finally
        {
            _server = null;
            Binding = null;
            if (_listener is not null) _listener.ConnectionRequested -= OnConnectionRequested;
            _listener = null;
            if (_publisher is not null)
            {
                _publisher.StatusChanged -= OnPublisherChanged;
                _publisher.Stop();
                _publisher = null;
            }
            lock (_peerGate)
            {
                foreach (var peer in _peers) peer.Dispose();
                _peers.Clear();
            }
            // A queued callback can still hold this CTS. Do not dispose it underneath that callback.
            _session = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
