using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EasyClipboard;
using EasyClipboard.HotspotProbe;

internal static class ReceiverChecks
{
    public static void RunPixelPreservationChecks()
    {
        foreach (var dpi in new[] { 96, 144, 72, 192, 300 })
        {
            using var original = new Bitmap(120, 80, PixelFormat.Format32bppArgb);
            original.SetResolution(dpi, dpi);
            for (var y = 0; y < original.Height; y++)
                for (var x = 0; x < original.Width; x++)
                    original.SetPixel(x, y, Color.FromArgb(255, (x * 17 + y * 3) % 256, (x * 5 + y * 11) % 256, (x * 13 + y * 7) % 256));
            using var encoded = new MemoryStream();
            original.Save(encoded, ImageFormat.Png);
            var received = TransferValidation.Prepare(new TransferRequest(1, Guid.NewGuid(), "image/png",
                DataBase64: Convert.ToBase64String(encoded.ToArray())));
            using var outputStream = new MemoryStream(received.Png!);
            using var output = new Bitmap(outputStream);
            if (output.Size != original.Size) throw new Exception($"FAIL: {dpi} DPI image dimensions changed.");
            for (var y = 0; y < original.Height; y++)
                for (var x = 0; x < original.Width; x++)
                    if (output.GetPixel(x, y).ToArgb() != original.GetPixel(x, y).ToArgb())
                        throw new Exception($"FAIL: {dpi} DPI image pixels changed at ({x}, {y}); unchanged canvas dimensions alone are insufficient.");
            Console.WriteLine($"PASS: {dpi} DPI PNG preserves every pixel and all four canvas edges.");
        }
    }

    public static async Task RunRestartAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".tools", "receiver-checks", Guid.NewGuid().ToString("N"));
        using var identity = new Identity(root);
        var clipboard = new FakeClipboard();
        var bank = new HistoryBank(root);
        var service = new TransferService(bank, clipboard);
        var binding = new HotspotBinding("restart-checks", "loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        var port = FreePort();
        using var local = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        // A second loopback source exercises peer diagnostics without using a real phone or network.
        using var peer = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                    await socket.ConnectAsync(context.DnsEndPoint, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        }) { BaseAddress = local.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
        for (var cycle = 1; cycle <= 5; cycle++)
        {
            var activity = System.Threading.Channels.Channel.CreateUnbounded<ReceiverActivity>();
            await using (var server = new ReceiverServer(binding, identity, service, port, present: () => true, allowHomeHttp: true))
            {
                server.PeerActivity += _ => throw new InvalidOperationException("Diagnostic display failed.");
                server.PeerActivity += value => activity.Writer.TryWrite(value);
                await server.StartAsync();
                using var selfCheck = await local.GetAsync("/");
                if (!selfCheck.IsSuccessStatusCode || activity.Reader.TryRead(out _))
                    throw new Exception("Local health check must not count as a phone request.");

                async Task VerifyActivity(string operation, int status, long count)
                {
                    var arrived = await activity.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    var finished = await activity.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    if (arrived.Finished || arrived.ActiveRequests != 1 || arrived.RequestCount != count ||
                        !finished.Finished || finished.ActiveRequests != 0 || finished.RequestCount != count ||
                        finished.StatusCode != status || arrived.Operation != operation || finished.Operation != operation)
                        throw new Exception("Peer diagnostics did not track request arrival and completion correctly.");
                    if (JsonSerializer.Serialize(finished).Contains(identity.Token)) throw new Exception("Diagnostic leaked credentials.");
                }
                using var page = await peer.GetAsync("/");
                if (!page.IsSuccessStatusCode) throw new Exception("Page check failed after restart.");
                await VerifyActivity("Page check", 200, 1);
                peer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
                using var transfer = await peer.PostAsJsonAsync("/v1/transfers",
                    new TransferRequest(1, Guid.NewGuid(), "text/plain", "Restart test"));
                if (!transfer.IsSuccessStatusCode) throw new Exception("Authenticated transfer failed after restart.");
                await VerifyActivity("Transfer", 200, 2);
                peer.DefaultRequestHeaders.Authorization = null;
                using var denied = await peer.PostAsJsonAsync("/v1/transfers",
                    new TransferRequest(1, Guid.NewGuid(), "text/plain", "Must not save"));
                if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Restart bypassed authentication.");
                await VerifyActivity("Transfer", 401, 3);
            }
            using var stopped = new TcpClient();
            try
            {
                await stopped.ConnectAsync(IPAddress.Loopback, port);
                throw new Exception("Receiver still listening after Stop.");
            }
            catch (SocketException) { }
            if (clipboard.Calls != cycle || bank.List().Count != cycle) throw new Exception("Restart duplicated or lost a transfer.");
            Console.WriteLine($"PASS: receiver restart cycle {cycle}, pooled clients reconnect, peer diagnostics, auth, and saved receipts.");
        }
    }

    public static async Task RunTailscaleLocalAsync()
    {
        var binding = HotspotBinding.FindTailscaleInterfaces().Single();
        var root = Path.Combine(Environment.CurrentDirectory, ".tools", "tailscale-checks", Guid.NewGuid().ToString("N"));
        using var identity = new Identity(root);
        var service = new TransferService(new HistoryBank(root), new FakeClipboard());
        // Use a disposable port and test identity; don't touch the user's live receiver or clipboard.
        var port = FreePort();
        await using var server = new ReceiverServer(binding, identity, service, port, allowHomeHttp: true);
        await server.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://{binding.Address}:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        var page = await client.GetStringAsync("/");
        if (!page.Contains("Tailscale receiver is reachable")) throw new Exception("Tailscale binding check failed.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        using var invalid = new StringContent("{broken", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/transfers", invalid);
        if (response.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Tailscale authentication check failed.");
        Console.WriteLine("PASS: local listener and authenticated request on actual Tailscale interface. This does not test an iPhone or remote tunnel.");
    }

    public static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }
        var root = Path.Combine(Environment.CurrentDirectory, ".tools", "receiver-checks", Guid.NewGuid().ToString("N"));
        using var identity = new Identity(root);
        Check(identity.Authorizes("bearer " + identity.Token.ToLowerInvariant()), "authentication accepts scheme and hexadecimal case variations");
        Check(identity.Authorizes("  Bearer   " + identity.Token + "  "), "authentication accepts harmless outer and scheme whitespace");
        Check(identity.AuthenticationFailure("")?.Code == "authorization_missing", "missing header has a specific diagnostic");
        Check(identity.AuthenticationFailure(identity.Token)?.Code == "authorization_format", "bare key has a specific header-format diagnostic");
        Check(identity.AuthenticationFailure("Bearer Bearer " + identity.Token)?.Code == "pairing_key_length", "duplicated Bearer has a length diagnostic");
        Check(identity.AuthenticationFailure("Bearer " + new string('Z', 64))?.Code == "pairing_key_format", "invalid key characters have a specific diagnostic");
        var mismatch = identity.AuthenticationFailure("Bearer " + new string('0', 64));
        Check(mismatch?.Code == "pairing_key_mismatch" && !mismatch.Message.Contains(identity.Token) && !mismatch.Message.Contains(new string('0', 64)),
            "valid-format mismatch diagnostic discloses neither key");
        var bank = new HistoryBank(root);
        var clipboard = new FakeClipboard();
        var service = new TransferService(bank, clipboard);
        var binding = new HotspotBinding("checks", "loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        var setupPort = FreePort();
        var transferPort = FreePort();
        while (transferPort == setupPort) transferPort = FreePort();
        await using var server = new ReceiverServer(binding, identity, service, setupPort, transferPort, () => true);
        await server.StartAsync();
        using var handler = new HttpClientHandler();
        bool ValidateCertificate(X509Certificate2? cert, SslPolicyErrors errors)
        {
            // The test trusts ONLY this generated CA and retains name/date/EKU checks. No trust-store changes.
            if (cert is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(identity.Authority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
            return chain.Build(cert);
        }
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) => ValidateCertificate(cert, errors);
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{transferPort}"), Timeout = TimeSpan.FromSeconds(15) };
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{setupPort}"), Timeout = TimeSpan.FromSeconds(5) };
        var text = "Hello 世界 👋🏽\nSecond line\r\nآخر سطر";
        var request = new TransferRequest(1, Guid.NewGuid(), "text/plain", text);
        async Task<HttpResponseMessage> Send(TransferRequest value) => await client.PostAsJsonAsync("/v1/transfers", value, LocalFiles.Json);

        var missingAuth = await Send(request);
        Check(missingAuth.StatusCode == HttpStatusCode.Unauthorized, "HTTPS rejects missing credentials");
        using var missingAuthBody = JsonDocument.Parse(await missingAuth.Content.ReadAsStringAsync());
        Check(missingAuthBody.RootElement.GetProperty("error").GetProperty("code").GetString() == "authorization_missing",
            "HTTP response explains missing-header failure without credentials");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('0', 64));
        Check((await Send(request)).StatusCode == HttpStatusCode.Unauthorized, "HTTPS rejects invalid credentials");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        Check((await http.PostAsJsonAsync("/v1/transfers", request)).StatusCode == HttpStatusCode.UpgradeRequired, "HTTP cannot receive transfers");
        Check((await http.GetByteArrayAsync("/root.cer")).SequenceEqual(identity.Authority.RawData), "onboarding sends only public certificate");
        var profile = await http.GetStringAsync("/install.mobileconfig");
        Check(!profile.Contains(identity.Token) && !profile.Contains(identity.WifiPassword), "profile contains no pairing or Wi-Fi secrets");
        Check(profile.Contains("com.apple.security.root"), "onboarding emits a certificate profile, not a fake Shortcut");

        service.Changed += () => throw new InvalidOperationException("History display failed.");
        var notified = false;
        service.Changed += () => notified = true;
        var response = await Send(request);
        Check(response.IsSuccessStatusCode, "text transfer completes over verified local HTTPS");
        Check(notified, "history-display failure preserves the successful receipt and other observers");
        Check(response.Content.Headers.ContentLength == (await response.Content.ReadAsByteArrayAsync()).Length &&
            response.Headers.ConnectionClose == true && response.Headers.TransferEncodingChunked != true,
            "success receipt has an exact byte length and completes the connection");
        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(receipt.RootElement.GetProperty("clipboardUpdated").GetBoolean(), "success confirms save and clipboard sink completion");
        Check(clipboard.Current?.Text == text, "Unicode, emoji, and newlines preserved");
        var calls = clipboard.Calls;
        var duplicate = await Send(request);
        Check(duplicate.IsSuccessStatusCode && clipboard.Calls == calls && bank.List().Count == 1, "duplicate adds no history and does not overwrite a later clipboard");
        Check((await Send(request with { Text = "different" })).StatusCode == HttpStatusCode.Conflict, "same ID with different content is rejected");
        var restarted = new HistoryBank(root);
        Check(restarted.Find(request.TransferId)?.Text == text && restarted.Find(request.TransferId)!.Applied, "encrypted history and receipt survive reopening");
        var historyBytes = File.ReadAllBytes(Directory.GetFiles(Path.Combine(root, "history"), "*.dat")[0]);
        Check(!Encoding.UTF8.GetString(historyBytes).Contains("Second line"), "history is not stored as plaintext");

        var before = clipboard.Current;
        var badImage = new TransferRequest(1, Guid.NewGuid(), "image/png", DataBase64: Convert.ToBase64String("not a PNG"u8));
        Check((await Send(badImage)).StatusCode == HttpStatusCode.BadRequest && clipboard.Current == before, "malformed image preserves previous clipboard");
        Check((await Send(badImage with { DataBase64 = "%%%" })).StatusCode == HttpStatusCode.BadRequest, "invalid base64 is rejected");
        Check((await Send(request with { TransferId = Guid.NewGuid(), Text = new string('x', 1024 * 1024 + 1) })).StatusCode == HttpStatusCode.BadRequest && clipboard.Current == before,
            "oversized text preserves clipboard");
        Check((await Send(request with { TransferId = Guid.NewGuid(), Html = "<img src='https://example.invalid/private'>" })).StatusCode == HttpStatusCode.BadRequest, "HTML cannot introduce external resources");
        service.Paused = true;
        Check((await Send(request with { TransferId = Guid.NewGuid() })).StatusCode == HttpStatusCode.ServiceUnavailable && clipboard.Current == before, "pause preserves clipboard");
        service.Paused = false;
        clipboard.Fail = true;
        var retry = request with { TransferId = Guid.NewGuid(), Text = "Retry safely" };
        Check((await Send(retry)).StatusCode == HttpStatusCode.ServiceUnavailable && clipboard.Current == before, "clipboard contention reports failure and preserves prior item");
        Check(bank.ListEntries().All(x => x.Id != retry.TransferId) && bank.ListEntries(true).Any(x => x.Id == retry.TransferId),
            "history list hides a pending transfer until clipboard commit");
        clipboard.Fail = false;
        Check((await Send(retry)).IsSuccessStatusCode && bank.List().Count == 2, "same-ID retry completes one pending entry");

        using var bitmap = new Bitmap(3, 2);
        bitmap.SetPixel(1, 1, Color.Magenta);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var image = new TransferRequest(1, Guid.NewGuid(), "image/png", DataBase64: Convert.ToBase64String(png.ToArray()));
        Check((await Send(image)).IsSuccessStatusCode && clipboard.Current?.Png is not null, "actual image is decoded and stored");
        using var savedImage = System.Drawing.Image.FromStream(new MemoryStream(new HistoryBank(root).Find(image.TransferId)!.Png!));
        Check(savedImage.Width == 3 && savedImage.Height == 2, "photo remains decodable after history reopen");

        var dropRoot = Path.Combine(root, "taildrop-check");
        var dropHistory = new HistoryBank(dropRoot);
        var dropSink = new FakeClipboard();
        var downloads = Directory.CreateDirectory(Path.Combine(dropRoot, "Downloads")).FullName;
        var dropId = Guid.NewGuid();
        var name = $"EasyClipboard-temp-{dropId:D}.jpeg";
        var tagged = Path.Combine(downloads, name);
        var ordinary = Path.Combine(downloads, "keep.jpeg");
        bitmap.Save(tagged, ImageFormat.Jpeg);
        File.Copy(tagged, ordinary);
        File.SetLastWriteTimeUtc(tagged, DateTime.UtcNow.AddSeconds(-10));
        var importerType = typeof(HistoryBank).Assembly.GetType("EasyClipboard.TaildropImport", throwOnError: true)!;
        var importer = Activator.CreateInstance(importerType, dropHistory, new TransferService(dropHistory, dropSink), downloads)!;
        Task ScanDrop(bool enabled) => (Task)importerType.GetMethod("RunAsync")!.Invoke(importer, [enabled])!;
        await ScanDrop(false);
        Check(File.Exists(tagged) && dropSink.Calls == 0, "Taildrop import requires opt-in");
        await ScanDrop(true);
        var managed = Path.Combine(dropRoot, "taildrop-temp", name);
        Check(File.Exists(managed) && !File.Exists(tagged) && File.Exists(ordinary) &&
            dropSink.Current?.Id == dropId && dropSink.Current.Png is not null && dropHistory.Find(dropId)?.Applied == true,
            "tagged JPEG lands on the clipboard and moves to managed storage while ordinary downloads stay put");
        File.SetLastWriteTimeUtc(managed, DateTime.UtcNow.AddHours(-25));
        await ScanDrop(false);
        Check(File.Exists(managed) && dropHistory.Find(dropId) is not null,
            "Taildrop images older than 24 hours stay within the normal history limits");
        dropHistory.Configure(new Preferences(30, 1, 32));
        dropHistory.Save(TransferValidation.Prepare(request) with { Applied = true, ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        dropHistory.Prune();
        await ScanDrop(false);
        Check(!File.Exists(managed) && dropHistory.Find(dropId) is null && File.Exists(ordinary),
            "shared history limits remove the managed Taildrop copy even when new imports are disabled");

        var cleanupHistory = new HistoryBank(Path.Combine(root, "duplicate-check"));
        var oldText = TransferValidation.Prepare(request) with { Applied = true };
        var newText = oldText with { Id = Guid.NewGuid(), ReceivedAt = oldText.ReceivedAt.AddSeconds(1) };
        var richText = newText with { Id = Guid.NewGuid(), Html = "<b>Different formatting</b>" };
        var pendingText = newText with { Id = Guid.NewGuid(), Applied = false };
        var oldImage = TransferValidation.Prepare(image) with { Applied = true };
        var newImage = oldImage with { Id = Guid.NewGuid(), ReceivedAt = oldImage.ReceivedAt.AddSeconds(1) };
        foreach (var item in new[] { oldText, newText, richText, pendingText, oldImage, newImage })
            cleanupHistory.Save(item);
        var reopenedCleanup = new HistoryBank(cleanupHistory.Root);
        Check(reopenedCleanup.ClearDuplicates() == 2 &&
            reopenedCleanup.ListEntries(true).Select(x => x.Id).ToHashSet()
                .SetEquals([newText.Id, richText.Id, pendingText.Id, newImage.Id]) &&
            reopenedCleanup.Find(oldText.Id) is null && reopenedCleanup.Find(oldImage.Id) is null,
            "clearing duplicates keeps the newest identical text and PNG, distinct formatting, and pending transfers");

        using var malformed = new StringContent("{broken", Encoding.UTF8, "application/json");
        Check((await client.PostAsync("/v1/transfers", malformed)).StatusCode == HttpStatusCode.BadRequest, "malformed JSON returns a clear error");
        var oldToken = identity.Token;
        identity.RotateToken();
        Check(!identity.Authorizes("Bearer " + oldToken), "revocation invalidates existing key");
        Check((await Send(request)).StatusCode == HttpStatusCode.Unauthorized, "revocation takes effect without receiver restart");
        using var reloaded = new Identity(root);
        Check(reloaded.Token == identity.Token && reloaded.Ssid == identity.Ssid, "pairing and hotspot identity survive restart");

        // An authenticated, incomplete HTTPS upload never reaches the transaction.
        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync(IPAddress.Loopback, transferPort);
            using var tls = new SslStream(socket.GetStream(), false, (_, cert, _, errors) => ValidateCertificate(cert as X509Certificate2, errors));
            await tls.AuthenticateAsClientAsync("127.0.0.1");
            await tls.WriteAsync(Encoding.ASCII.GetBytes($"POST /v1/transfers HTTP/1.1\r\nHost: localhost\r\nAuthorization: Bearer {identity.Token}\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{{\""));
        }
        Check(bank.List().Count == 3, "interrupted request adds no successful history");
        bank.Configure(new Preferences(30, 1, 32));
        Check(bank.List().Count == 1, "retention prunes to configured count");
        Check(bank.ListEntries().Single().Id == bank.List().Single().Id, "pruning removes the history row and its saved payload together");
        bank.Clear();
        Check(bank.ListEntries().Count == 0, "clear removes history rows immediately");
        Check(new HistoryBank(root).List().Count == 0, "clear history persists");
        // Explicitly opted-in home HTTP mode uses the same validation, auth, and persistence pipeline.
        var homePort = FreePort();
        var unusedTlsPort = FreePort();
        while (unusedTlsPort == homePort) unusedTlsPort = FreePort();
        var homePresent = true;
        await using (var homeServer = new ReceiverServer(binding, identity, service, homePort, unusedTlsPort,
            () => homePresent, allowHomeHttp: true))
        {
            await homeServer.StartAsync();
            using var home = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{homePort}"), Timeout = TimeSpan.FromSeconds(5) };
            Check((await home.PostAsJsonAsync("/v1/transfers", request)).StatusCode == HttpStatusCode.Unauthorized,
                "home HTTP still requires pairing credentials");
            home.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('0', 64));
            Check((await home.PostAsJsonAsync("/v1/transfers", request)).StatusCode == HttpStatusCode.Unauthorized,
                "home HTTP rejects invalid pairing key");
            home.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
            Check((await home.PostAsJsonAsync("/v1/transfers", request)).IsSuccessStatusCode && clipboard.Current?.Text == text,
                "opted-in home HTTP transfers Unicode text without TLS");
            Check((await home.PostAsJsonAsync("/v1/transfers", image)).IsSuccessStatusCode && clipboard.Current?.Png is not null,
                "home HTTP transfers actual photo data");
            Check((await home.GetAsync("/install.mobileconfig")).StatusCode == HttpStatusCode.NotFound,
                "home HTTP requires and serves no certificate profile");
            var homeCalls = clipboard.Calls;
            Check((await home.PostAsJsonAsync("/v1/transfers", image)).IsSuccessStatusCode && clipboard.Calls == homeCalls,
                "home HTTP retries do not overwrite clipboard");
            homePresent = false;
            Check((await home.PostAsJsonAsync("/v1/transfers", request)).StatusCode == HttpStatusCode.ServiceUnavailable,
                "home HTTP rejects requests after selected interface disappears");
        }
        Console.WriteLine($"{passed} receiver checks passed. Clipboard sink is simulated; no live iPhone, AP, or Windows paste test was performed.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FakeClipboard : IClipboardSink
    {
        public ClipItem? Current;
        public int Calls;
        public bool Fail;
        public Task ApplyAsync(ClipItem item, Action commit)
        {
            if (Fail) throw new TransferError(503, "clipboard_busy", "Simulated clipboard contention.");
            var previous = Current;
            try { Current = item; commit(); Calls++; }
            catch { Current = previous; throw; }
            return Task.CompletedTask;
        }
    }
}
