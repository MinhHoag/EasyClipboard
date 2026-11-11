using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyClipboard;

public sealed record TransferRequest(int Version, Guid TransferId, string ContentType, string? Text = null,
    string? DataBase64 = null, string? Html = null);
public sealed record ClipItem(Guid Id, DateTimeOffset ReceivedAt, string ContentType, string? Text,
    byte[]? Png, string? Html, string Hash, bool Applied = false)
{
    public override string ToString() => $"{ReceivedAt.LocalDateTime:g}   " +
        (Text is null ? "Photo / screenshot" : Text.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(70, Text.Length)]);
}
public sealed class TransferError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class TransferValidation
{
    public static ClipItem Prepare(TransferRequest request)
    {
        if (request.Version != 1) throw new TransferError(400, "unsupported_version", "Use protocol version 1.");
        if (request.TransferId == Guid.Empty) throw new TransferError(400, "invalid_id", "Provide a nonempty UUID transferId.");
        string? text = null;
        byte[]? png = null;
        if (request.ContentType == "text/plain")
        {
            text = request.Text;
            if (string.IsNullOrEmpty(text) || text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > 1024 * 1024 || request.DataBase64 is not null)
                throw new TransferError(400, "invalid_text", "Text must be nonempty, at most 1 MB UTF-8, without NUL or image data.");
            if (request.Html is { } html && (html.Length > 1024 * 1024 ||
                Regex.IsMatch(Regex.Replace(html, @"</?(?:p|br|b|strong|i|em|u|ol|ul|li|pre|code|div|blockquote|h[1-6])\s*/?>", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)), "[<>]")))
                throw new TransferError(400, "invalid_html", "Optional HTML supports basic formatting tags only, without attributes or external resources.");
        }
        else if (request.ContentType == "image/png")
        {
            if (request.Text is not null || request.Html is not null || string.IsNullOrEmpty(request.DataBase64))
                throw new TransferError(400, "invalid_image", "Provide a single base64 PNG. Convert other image formats to PNG in Shortcuts.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(request.DataBase64); }
            catch (FormatException) { throw new TransferError(400, "invalid_image", "Image data is not valid base64."); }
            if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
                !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
                throw new TransferError(400, "invalid_image", "Expected actual PNG image data, not a URL or filename.");
            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            ImageResources.EnsureImageFits(width, height, bytes.Length);
            try
            {
                using var input = new MemoryStream(bytes);
                using var decoded = Image.FromStream(input, false, true);
                if (decoded.RawFormat.Guid != ImageFormat.Png.Guid || decoded.Width != width || decoded.Height != height)
                    throw new ArgumentException();
                // Fully rasterize before touching storage or the clipboard. Drops unneeded metadata.
                using var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // DrawImageUnscaled uses physical (DPI-based) size. A 144-DPI screenshot
                    // would otherwise shrink inside this 96-DPI canvas. Copy pixel rectangles
                    // explicitly so sanitizing the PNG never resizes or crops its contents.
                    graphics.PageUnit = GraphicsUnit.Pixel;
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    var pixels = new Rectangle(0, 0, decoded.Width, decoded.Height);
                    graphics.DrawImage(decoded, pixels, pixels, GraphicsUnit.Pixel);
                }
                using var output = new MemoryStream();
                bitmap.Save(output, ImageFormat.Png);
                png = output.ToArray();
            }
            catch (OutOfMemoryException) { throw ImageResources.InsufficientMemory(); }
            catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.ExternalException)
            { throw new TransferError(400, "invalid_image", "The PNG could not be decoded."); }
        }
        else throw new TransferError(415, "unsupported_type", "Use text/plain or image/png.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, LocalFiles.Json)));
        return new ClipItem(request.TransferId, DateTimeOffset.UtcNow, request.ContentType, text, png, request.Html, hash);
    }
}

public interface IClipboardSink
{
    Task ApplyAsync(ClipItem item, Action commit);
}

public sealed class TransferService(HistoryBank bank, IClipboardSink clipboard)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _paused;
    public bool Paused { get => _paused; set => _paused = value; }
    public event Action? Changed;
    public async Task<bool> ReceiveAsync(TransferRequest request)
    {
        if (Paused) throw new TransferError(503, "paused", "Receiving is paused on the laptop.");
        // Bound decode concurrency as well as writes. Never queue large decoded images indefinitely.
        if (!await _gate.WaitAsync(0)) throw new TransferError(429, "busy", "Another transfer is being processed. Retry this transferId.");
        try
        {
            if (Paused) throw new TransferError(503, "paused", "Receiving is paused on the laptop.");
            var item = TransferValidation.Prepare(request);
            var previous = bank.Find(item.Id);
            if (previous is not null)
            {
                if (previous.Hash != item.Hash) throw new TransferError(409, "id_conflict", "This transferId was used for different content.");
                if (previous.Applied) return true;
                item = previous;
            }
            bank.Save(item);
            await clipboard.ApplyAsync(item, () => bank.Save(item with { Applied = true }));
            // Clipboard and receipt have committed. Maintenance must not turn that into a failed transfer.
            bank.TryPrune(item.Id);
            Changed?.Invoke();
            return false;
        }
        catch (IOException) { throw new TransferError(507, "storage_failed", "Local history could not be saved. Check free disk space and retry."); }
        catch (UnauthorizedAccessException) { throw new TransferError(507, "storage_failed", "The local history folder is not writable."); }
        finally { _gate.Release(); }
    }
}
