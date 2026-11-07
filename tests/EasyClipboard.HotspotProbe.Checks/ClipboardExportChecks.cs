using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using EasyClipboard;
using EasyClipboard.HotspotProbe;

internal static class ClipboardExportChecks
{
    public static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }
        var root = Path.Combine(Environment.CurrentDirectory, ".tools", "export-checks", Guid.NewGuid().ToString("N"));
        using var identity = new Identity(root);
        var bank = new HistoryBank(root);
        var sink = new UnusedSink();
        var source = new FakeSource();
        var export = new ClipboardExport(source);
        var binding = new HotspotBinding("export-checks", "loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        var setupPort = FreePort();
        var tlsPort = FreePort();
        while (tlsPort == setupPort) tlsPort = FreePort();
        await using var server = new ReceiverServer(binding, identity, new TransferService(bank, sink), setupPort, tlsPort,
            () => true, allowHomeHttp: true, clipboardExport: export);
        await server.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{setupPort}"), Timeout = TimeSpan.FromSeconds(5) };
        async Task<HttpResponseMessage> Get() => await client.GetAsync("/v1/clipboard");
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.Unauthorized && source.Calls == 0, "fetch rejects missing auth without reading clipboard");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.Forbidden && source.Calls == 0, "fetch starts disabled even with valid credentials");
        export.Enabled = true;
        source.Current = new(Text: "Hello 世界 👋\nSecond line");
        using (var response = await Get())
        {
            Check(response.IsSuccessStatusCode && await response.Content.ReadAsStringAsync() == source.Current.Text, "fetch returns current Unicode text directly");
            Check(response.Content.Headers.ContentType?.ToString() == "text/plain; charset=utf-8", "text download has UTF-8 MIME type");
            Check(response.Headers.CacheControl?.NoStore == true && response.Content.Headers.ContentLength == Encoding.UTF8.GetByteCount(source.Current.Text!), "fetch is not cached and declares exact byte length");
        }
        source.Current = new(Text: "New clipboard value");
        using (var response = await Get()) Check(await response.Content.ReadAsStringAsync() == source.Current.Text, "each fetch reads fresh clipboard content");
        using (var image = new Bitmap(9, 7))
        {
            image.SetResolution(144, 144);
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++) image.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Magenta : Color.Green);
            using var bytes = new MemoryStream();
            image.Save(bytes, ImageFormat.Png);
            source.Current = new(Png: bytes.ToArray());
            using var response = await Get();
            var body = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(body);
            using var result = new Bitmap(stream);
            Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/png", "fetch returns raw PNG without JSON or base64");
            var same = image.Size == result.Size;
            for (var y = 0; y < image.Height && same; y++)
                for (var x = 0; x < image.Width && same; x++) same = image.GetPixel(x, y).ToArgb() == result.GetPixel(x, y).ToArgb();
            Check(same, "fetched 144-DPI image preserves every pixel");
        }
        Check(bank.List(true).Count == 0 && sink.Calls == 0, "fetch never saves history or changes the PC clipboard");
        source.Current = new();
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.Conflict && (await response.Content.ReadAsStringAsync()).Contains("clipboard_empty"), "empty clipboard returns a structured failure");
        source.Current = new(Png: [1, 2, 3]);
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.BadRequest, "invalid source image is rejected");
        source.Current = new(Png: new byte[TransferValidation.MaxImageBytes + 1]);
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.RequestEntityTooLarge, "source PNG has a bounded size");
        source.Current = new(Text: "bad\0text");
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.BadRequest, "source text follows existing validation");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ClipboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Read = _ => { started.TrySetResult(); return release.Task; };
        var pending = Get();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (var busy = await Get()) Check(busy.StatusCode == HttpStatusCode.TooManyRequests, "overlapping fetches are bounded without reading twice");
        export.Enabled = false;
        export.Enabled = true;
        release.SetResult(new(Text: "Must not escape a withdrawn session"));
        using (var response = await pending) Check(response.StatusCode == HttpStatusCode.Forbidden && !(await response.Content.ReadAsStringAsync()).Contains("Must not escape"), "disable and re-enable invalidates an in-flight fetch");
        source.Read = null;
        source.Current = new(Text: "After re-enable");
        using (var response = await Get()) Check(response.IsSuccessStatusCode, "a fresh fetch works after re-enable");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Read = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(); };
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = export.ReadAsync(cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await cancelled; Check(false, "cancelled fetch must not return content"); }
            catch (OperationCanceledException) { Check(true, "cancelling a fetch returns no content"); }
        }
        source.Read = null;
        using (var response = await Get()) Check(response.IsSuccessStatusCode, "cancelled fetch releases the read gate");
        var readCount = source.Calls;
        identity.RotateToken();
        using (var response = await Get()) Check(response.StatusCode == HttpStatusCode.Unauthorized && source.Calls == readCount, "revoked credentials cannot read PC clipboard");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        using (var response = await client.PostAsync("/v1/clipboard", null)) Check(response.StatusCode == HttpStatusCode.MethodNotAllowed && source.Calls == readCount, "fetch does not expose a write or POST action");
        using (var request = new HttpRequestMessage(HttpMethod.Head, "/v1/clipboard"))
        using (var response = await client.SendAsync(request)) Check(response.StatusCode == HttpStatusCode.MethodNotAllowed && source.Calls == readCount, "HEAD does not read the clipboard");

        var insecurePort = FreePort();
        var securePort = FreePort();
        while (securePort == insecurePort) securePort = FreePort();
        await using var httpsOnly = new ReceiverServer(binding, identity, new TransferService(bank, sink), insecurePort, securePort,
            () => true, clipboardExport: export);
        await httpsOnly.StartAsync();
        using var insecure = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{insecurePort}") };
        insecure.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        using (var response = await insecure.GetAsync("/v1/clipboard")) Check(response.StatusCode == HttpStatusCode.UpgradeRequired && source.Calls == readCount, "HTTPS modes reject plaintext fetch before reading clipboard");
        Console.WriteLine($"{passed} PC-to-phone checks passed; clipboard source/sink are simulated.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    private sealed class FakeSource : IClipboardSource
    {
        public ClipboardSnapshot Current = new();
        public Func<CancellationToken, Task<ClipboardSnapshot>>? Read;
        public int Calls;
        public Task<ClipboardSnapshot> ReadAsync(CancellationToken token) { Calls++; return Read?.Invoke(token) ?? Task.FromResult(Current); }
    }
    private sealed class UnusedSink : IClipboardSink
    {
        public int Calls;
        public Task ApplyAsync(ClipItem item, Action commit) { Calls++; commit(); return Task.CompletedTask; }
    }
}
