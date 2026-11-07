using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EasyClipboard.HotspotProbe;

public sealed record HotspotBinding(string InterfaceId, string InterfaceName, IPAddress Address, IPAddress Mask)
{
    public bool IsTailscale { get; init; }
    public override string ToString() => $"{InterfaceName} — {Address}";

    public static bool IsTailscaleV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }

    public static IReadOnlyList<HotspotBinding> FindTailscaleInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            (adapter.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)))
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
            .Where(ip => IsTailscaleV4(ip.Address))
            .Select(ip => new HotspotBinding(adapter.Id, adapter.Name, ip.Address, ip.IPv4Mask) { IsTailscale = true }))
        .ToArray();

    public static IReadOnlyList<HotspotBinding> FindHomeInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
            !adapter.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
            .Where(ip => IsPrivateV4(ip.Address) && !ip.IPv4Mask.Equals(IPAddress.Any))
            .Select(ip => new HotspotBinding(adapter.Id, adapter.Name, ip.Address, ip.IPv4Mask)))
        .OrderBy(binding => binding.InterfaceName).ToArray();

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        // Tailscale's virtual adapter may have a /32 mask. Authenticated peers are other
        // 100.64/10 nodes, not members of that /32. Never apply this policy to a LAN binding.
        if (IsTailscale) return IsTailscaleV4(address);
        var local = Address.GetAddressBytes();
        var remote = address.GetAddressBytes();
        var mask = Mask.GetAddressBytes();
        return remote.Where((value, i) => (value & mask[i]) != (local[i] & mask[i])).Any() == false;
    }

    public bool IsPresent() => NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
        adapter.Id.Equals(InterfaceId, StringComparison.OrdinalIgnoreCase) &&
        adapter.OperationalStatus == OperationalStatus.Up &&
        adapter.GetIPProperties().UnicastAddresses.Any(ip => ip.Address.Equals(Address)));

    public static bool IsPrivateV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    public static IReadOnlyList<HotspotBinding> FindActive()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                .Where(ip => IsPrivateV4(ip.Address))
                .Select(ip => new HotspotBinding(adapter.Id, adapter.Name, ip.Address, ip.IPv4Mask)))
            .ToArray();
    }
}

/// <summary>Harmless HTTP reachability check. Never accepts clipboard contents or pairing secrets.</summary>
public sealed class ProbeServer : IAsyncDisposable
{
    public const int DefaultPort = 47831;
    private readonly WebApplication _app;
    public string ProbeId { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    public event Action? PeerReached;

    public ProbeServer(HotspotBinding binding, int port = DefaultPort, Func<bool>? isPresent = null)
    {
        isPresent ??= binding.IsPresent;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 1024;
            options.Limits.MaxConcurrentConnections = 8;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
            // Bind only the AP interface's address. Never listen on 0.0.0.0 or the school LAN.
            options.Listen(binding.Address, port);
        });
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var remote = context.Connection.RemoteIpAddress;
            if (!isPresent() || remote is null || !binding.Contains(remote))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            // There is no upload facility, including on GET requests.
            if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                // Do not reuse a connection whose rejected body has not been read.
                context.Response.Headers.Connection = "close";
                context.Response.ContentLength = 0;
                return;
            }
            await next(context);
        });
        _app.MapGet("/v1/probe", (HttpContext context) =>
        {
            // A laptop checking its own AP IP is not evidence of a phone connection.
            if (!binding.Address.Equals(context.Connection.RemoteIpAddress)) PeerReached?.Invoke();
            return Results.Json(new
            {
                version = 1,
                kind = "connectivity-probe",
                probeId = ProbeId,
                message = "Connected to EasyClipboard's private network. Clipboard transfer is not implemented yet."
            });
        });
    }

    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _app.StopAsync(timeout.Token); }
        finally { await _app.DisposeAsync(); }
    }
}
