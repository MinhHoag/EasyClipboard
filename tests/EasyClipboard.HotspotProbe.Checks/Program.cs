using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasyClipboard.HotspotProbe;

if (args.Contains("--image-pixels"))
{
    ReceiverChecks.RunPixelPreservationChecks();
    return;
}

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
    passed++;
}

var binding = new HotspotBinding("test", "test", IPAddress.Parse("192.168.72.1"), IPAddress.Parse("255.255.255.0"));
Check(binding.Contains(IPAddress.Parse("192.168.72.2")), "accept AP subnet");
Check(!binding.Contains(IPAddress.Parse("192.168.73.2")), "reject other subnet");
Check(!binding.Contains(IPAddress.IPv6Loopback), "reject IPv6 on IPv4 endpoint");
Check(!HotspotBinding.IsPrivateV4(IPAddress.Parse("8.8.8.8")), "reject public address");
Check(!HotspotBinding.IsPrivateV4(IPAddress.Parse("169.254.1.1")), "reject unconfigured link-local address");
Check(HotspotBinding.IsPrivateV4(IPAddress.Parse("172.16.0.1")), "accept private 172.16 address");
Check(!HotspotBinding.IsPrivateV4(IPAddress.Parse("172.32.0.1")), "reject address outside private 172 range");
Check(HotspotBinding.IsTailscaleV4(IPAddress.Parse("100.64.0.1")), "accept start of Tailscale IPv4 range");
Check(HotspotBinding.IsTailscaleV4(IPAddress.Parse("100.127.255.254")), "accept end of Tailscale IPv4 range");
Check(!HotspotBinding.IsTailscaleV4(IPAddress.Parse("100.63.255.255")), "reject address below Tailscale range");
Check(!HotspotBinding.IsTailscaleV4(IPAddress.Parse("100.128.0.1")), "reject address above Tailscale range");
var tailnet = new HotspotBinding("test-only", "Tailscale", IPAddress.Parse("100.70.0.1"), IPAddress.Parse("255.255.255.255")) { IsTailscale = true };
Check(tailnet.Contains(IPAddress.Parse("100.85.12.34")), "accept Tailscale peer despite virtual adapter /32 mask");
Check(!tailnet.Contains(IPAddress.Parse("192.168.72.2")), "Tailscale policy rejects ordinary LAN source");
Check(!binding.Contains(IPAddress.Parse("100.85.12.34")), "LAN policy does not inherit Tailscale peer access");
Check(!HotspotBinding.IsPrivateV4(IPAddress.Parse("100.70.0.1")), "Tailscale range stays out of ordinary home discovery");

// These tests intentionally bind loopback, never create an AP or change a firewall.
var reservation = new TcpListener(IPAddress.Loopback, 0);
reservation.Start();
var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
reservation.Stop();
var loopback = new HotspotBinding("test-only", "loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
var present = true;
var peers = 0;
await using (var server = new ProbeServer(loopback, port, () => present))
{
    server.PeerReached += () => peers++;
    await server.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
    var response = await client.GetAsync("/v1/probe");
    Check(response.StatusCode == HttpStatusCode.OK, "probe returns 200");
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Check(document.RootElement.GetProperty("probeId").GetString() == server.ProbeId, "session code matches");
    Check(document.RootElement.GetProperty("version").GetInt32() == 1, "versioned response");
    Check(!document.RootElement.TryGetProperty("password", out _), "no credential in response");
    Check(response.Headers.CacheControl?.NoStore == true, "response cannot be cached");
    Check(peers == 0, "self-check does not claim phone reached service");
    Check((await client.PostAsync("/v1/probe", null)).StatusCode == HttpStatusCode.MethodNotAllowed, "reject POST");
    Check((await client.GetAsync("/v1/transfers")).StatusCode == HttpStatusCode.NotFound, "no clipboard upload endpoint");
    using var upload = new HttpRequestMessage(HttpMethod.Get, "/v1/probe") { Content = new StringContent(new string('x', 4096), Encoding.UTF8) };
    Check((await client.SendAsync(upload)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "reject upload body");
    using var chunked = new HttpRequestMessage(HttpMethod.Get, "/v1/probe") { Content = new StringContent("test") };
    chunked.Headers.TransferEncodingChunked = true;
    Check((await client.SendAsync(chunked)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "reject chunked upload body");
    present = false;
    Check((await client.GetAsync("/v1/probe")).StatusCode == HttpStatusCode.ServiceUnavailable, "reject requests after AP address disappears");
}
using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    try
    {
        await socket.ConnectAsync(IPAddress.Loopback, port);
        Check(false, "server stops listening on disposal");
    }
    catch (SocketException) { Check(true, "server stops listening on disposal"); }
}
Console.WriteLine($"{passed} checks passed. These are local checks, not iPhone tests.");
await ReceiverChecks.RunAsync();
ReceiverChecks.RunPixelPreservationChecks();
await ReceiverChecks.RunRestartAsync();
await ClipboardExportChecks.RunAsync();
await LargeImageChecks.RunAsync();
if (args.Contains("--tailscale-local")) await ReceiverChecks.RunTailscaleLocalAsync();
