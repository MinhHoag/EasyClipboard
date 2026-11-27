using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using EasyClipboard;
using EasyClipboard.HotspotProbe;

internal static class LargeImageChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".tools", "large-image-checks", Guid.NewGuid().ToString("N"));
        using var identity = new Identity(root);
        var bank = new HistoryBank(root);
        var sink = new Sink();
        var source = new Source();
        var export = new ClipboardExport(source) { Enabled = true };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var binding = new HotspotBinding("large-image-checks", "loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        await using var server = new ReceiverServer(binding, identity, new TransferService(bank, sink),
            port, present: () => true, allowHomeHttp: true, clipboardExport: export);
        await server.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);

        foreach (var (width, height, noise, label) in new[]
        {
            (2304, 2304, true, "PNG over 12 MiB with JSON over 18 MiB"),
            (8000, 6000, false, "48-megapixel photo"),
            (32, 13000, false, "13000-pixel-tall screenshot")
        })
        {
            using var original = CreateImage(width, height, noise);
            using var png = new MemoryStream();
            original.Save(png, ImageFormat.Png);
            var bytes = png.ToArray();
            var base64 = Convert.ToBase64String(bytes);
            if (noise && (bytes.Length <= 12 * 1024 * 1024 || base64.Length <= 18 * 1024 * 1024))
                throw new Exception("The large-file fixture did not cross both old byte limits.");
            var request = new TransferRequest(1, Guid.NewGuid(), "image/png", DataBase64: base64);
            using var response = await client.PostAsJsonAsync("/v1/send-url", request, LocalFiles.Json);
            if (!response.IsSuccessStatusCode) throw new Exception($"FAIL: {label}: {await response.Content.ReadAsStringAsync()}");
            if (sink.Item?.Id != request.TransferId) throw new Exception("Large image never reached the clipboard sink.");
            AssertPixels(original, sink.Item.Png!);
            var reopened = new HistoryBank(root);
            var entry = reopened.ListEntries().Single(x => x.Id == request.TransferId);
            if (entry.Label != "Photo / screenshot") throw new Exception("Saved photo is missing its history label.");
            var saved = reopened.Find(entry.Id);
            if (saved is not { Applied: true }) throw new Exception("Large image receipt did not persist.");
            AssertPixels(original, saved.Png!);
            Console.WriteLine($"PASS: {label} transfers, saves encrypted history, and preserves every pixel at 144 DPI.");
            if (noise)
            {
                source.Png = bytes;
                using var fetched = await client.GetAsync("/v1/receive-url");
                if (!fetched.IsSuccessStatusCode) throw new Exception("Full-size PNG could not be fetched back from PC.");
                AssertPixels(original, await fetched.Content.ReadAsByteArrayAsync());
                source.Png = null;
                Console.WriteLine("PASS: full-resolution PC fetch also accepts PNG above the former 12 MiB cutoff.");
            }
        }

        // A forged enormous IHDR must fail before asking GDI+ to allocate it.
        var forged = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(forged, 0);
        "IHDR"u8.CopyTo(forged.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(forged.AsSpan(16), int.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(forged.AsSpan(20), int.MaxValue);
        var before = sink.Item;
        using (var response = await client.PostAsJsonAsync("/v1/send-url",
            new TransferRequest(1, Guid.NewGuid(), "image/png", DataBase64: Convert.ToBase64String(forged))))
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || sink.Item != before ||
                !(await response.Content.ReadAsStringAsync()).Contains("image_memory_unavailable"))
                throw new Exception("Resource failure must preserve the clipboard and explain PC memory.");
        Console.WriteLine("PASS: impossible image dimensions fail the resource check without changing the clipboard.");

        // An unfinished body holds admission. A second authenticated upload is rejected
        // before it can buffer its own image; aborting releases admission for the next one.
        using var pending = new TcpClient();
        await pending.ConnectAsync(IPAddress.Loopback, port);
        var stream = pending.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"POST /v1/send-url HTTP/1.1\r\nHost: localhost\r\nAuthorization: Bearer {identity.Token}\r\nContent-Type: application/json\r\nContent-Length: 100000\r\n\r\n{{\"version\":1,"));
        var busySeen = false;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/v1/send-url", new TransferRequest(1, Guid.NewGuid(), "text/plain", "admission check"));
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { busySeen = true; break; }
            await Task.Delay(20);
        }
        if (!busySeen) throw new Exception("An unfinished upload did not protect body-buffering admission.");
        pending.Close();
        var recovered = false;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/v1/send-url", new TransferRequest(1, Guid.NewGuid(), "text/plain", "after abort"));
            if (response.IsSuccessStatusCode) { recovered = true; break; }
            await Task.Delay(20);
        }
        if (!recovered) throw new Exception("Aborted body did not release upload admission.");
        Console.WriteLine("PASS: concurrent uploads are rejected before buffering; aborted uploads release admission.");
    }

    private static Bitmap CreateImage(int width, int height, bool noise)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(144, 144);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var random = new Random(7134);
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                if (noise) random.NextBytes(row);
                else for (var x = 0; x < width; x++)
                {
                    row[x * 4] = (byte)(x / 53);
                    row[x * 4 + 1] = (byte)(y / 41);
                    row[x * 4 + 2] = (byte)((x / 71 + y / 67) % 256);
                }
                for (var x = 0; x < width; x++) row[x * 4 + 3] = 255;
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static void AssertPixels(Bitmap expected, byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var actual = new Bitmap(stream);
        if (actual.Size != expected.Size) throw new Exception("Full-resolution image dimensions changed.");
        var rectangle = new Rectangle(Point.Empty, expected.Size);
        var left = expected.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var right = actual.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var a = new byte[expected.Width * 4];
                var b = new byte[a.Length];
                for (var y = 0; y < expected.Height; y++)
                {
                    Marshal.Copy(left.Scan0 + y * left.Stride, a, 0, a.Length);
                    Marshal.Copy(right.Scan0 + y * right.Stride, b, 0, b.Length);
                    if (!a.AsSpan().SequenceEqual(b)) throw new Exception($"Full-resolution image pixels changed on row {y}.");
                }
            }
            finally { actual.UnlockBits(right); }
        }
        finally { expected.UnlockBits(left); }
    }

    private sealed class Sink : IClipboardSink
    {
        public ClipItem? Item;
        public Task ApplyAsync(ClipItem item, Action commit) { commit(); Item = item; return Task.CompletedTask; }
    }
    private sealed class Source : IClipboardSource
    {
        public byte[]? Png;
        public Task<ClipboardSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(new ClipboardSnapshot(Png: Png));
    }
}
