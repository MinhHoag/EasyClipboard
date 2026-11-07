using System.Runtime.InteropServices;
using System.Text;

namespace EasyClipboard;

/// <summary>Runs on the Windows Forms STA thread. Incoming network threads never access OLE directly.</summary>
public sealed class WindowsClipboard(Control dispatcher) : IClipboardSink, IClipboardSource
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    public async Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        var completion = new TaskCompletionSource<ClipboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (dispatcher.IsDisposed) throw new TransferError(503, "closing", "The receiver is closing.");
                var sequence = GetClipboardSequenceNumber();
                var snapshot = ReadCurrent(token);
                if (sequence != GetClipboardSequenceNumber())
                    throw new TransferError(409, "clipboard_changed", "The PC clipboard changed during the fetch. Try again.");
                completion.TrySetResult(snapshot);
            }
            catch (ExternalException) { completion.TrySetException(new TransferError(503, "clipboard_busy", "Windows clipboard is busy. Try again.")); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        if (dispatcher.IsDisposed || !dispatcher.IsHandleCreated)
            throw new TransferError(503, "closing", "The receiver is closing.");
        try { if (dispatcher.InvokeRequired) dispatcher.BeginInvoke((Action)Run); else Run(); }
        catch (InvalidOperationException) { throw new TransferError(503, "closing", "The receiver is closing."); }
        try { return await completion.Task.WaitAsync(token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TransferError(503, "clipboard_busy", "Windows clipboard did not respond in time. Try again."); }
    }

    private static ClipboardSnapshot ReadCurrent(CancellationToken token)
    {
        var data = Clipboard.GetDataObject();
        if (data is null || data.GetFormats(false).Length == 0) return new();
        // Prefer actual PNG/image content over any accompanying HTML or source URL.
        if (data.GetDataPresent("PNG", false))
        {
            var raw = data.GetData("PNG", false);
            if (raw is byte[] bytes)
            {
                if (bytes.Length > TransferValidation.MaxImageBytes) throw ImageTooLarge();
                return new(Png: bytes);
            }
            if (raw is Stream stream)
            {
                var originalPosition = stream.CanSeek ? stream.Position : (long?)null;
                try
                {
                    if (stream.CanSeek) stream.Position = 0;
                    using var output = new MemoryStream();
                    var buffer = new byte[65536];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (output.Length + read > TransferValidation.MaxImageBytes) throw ImageTooLarge();
                        output.Write(buffer, 0, read);
                    }
                    return new(Png: output.ToArray());
                }
                finally { if (originalPosition is { } position) stream.Position = position; }
            }
        }
        if (Clipboard.ContainsImage())
        {
            using var image = Clipboard.GetImage();
            if (image is null) throw new TransferError(409, "clipboard_changed", "The PC clipboard changed. Try again.");
            if (image.Width > 12000 || image.Height > 12000 || (long)image.Width * image.Height > 20_000_000)
                throw new TransferError(413, "image_too_large", "The PC clipboard image exceeds 20 megapixels or 12000 pixels per side.");
            using var output = new MemoryStream();
            image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            if (output.Length > TransferValidation.MaxImageBytes) throw ImageTooLarge();
            return new(Png: output.ToArray());
        }
        if (data.GetDataPresent(DataFormats.UnicodeText))
            return new(Text: data.GetData(DataFormats.UnicodeText) as string);
        throw new TransferError(415, "unsupported_clipboard", "Copy plain text or an actual image on the PC. Files and other clipboard formats are not supported.");
    }

    private static TransferError ImageTooLarge() => new(413, "image_too_large", "The PC clipboard PNG exceeds 12 MB.");

    public Task ApplyAsync(ClipItem item, Action commit)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            try { Apply(item, commit); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }
        if (dispatcher.IsDisposed || !dispatcher.IsHandleCreated)
            return Task.FromException(new TransferError(503, "closing", "The receiver is closing."));
        try { if (dispatcher.InvokeRequired) dispatcher.BeginInvoke((Action)Run); else Run(); }
        catch (InvalidOperationException) { completion.TrySetException(new TransferError(503, "closing", "The receiver is closing.")); }
        return completion.Task;
    }

    private static void Apply(ClipItem item, Action commit)
    {
        var resources = new List<IDisposable>();
        try
        {
            var data = new DataObject();
            if (item.Text is not null)
            {
                data.SetText(item.Text, TextDataFormat.UnicodeText);
                if (item.Html is not null) data.SetData(DataFormats.Html, BuildHtml(item.Html));
            }
            else
            {
                var stream = new MemoryStream(item.Png!);
                using var decoded = Image.FromStream(stream);
                var bitmap = new Bitmap(decoded);
                resources.Add(stream);
                resources.Add(bitmap);
                data.SetData(DataFormats.Bitmap, true, bitmap);
                var pngStream = new MemoryStream(item.Png!);
                resources.Add(pngStream);
                data.SetData("PNG", false, pngStream);
            }

            // Materialize the old formats before replacing the OLE object; a lazy reference is not a backup.
            var previous = new DataObject();
            IDataObject? current = null;
            for (var attempt = 0; ; attempt++)
            {
                try { current = Clipboard.GetDataObject(); break; }
                catch (ExternalException) when (attempt < 9) { Thread.Sleep(75); }
            }
            if (current is not null)
                foreach (var format in current.GetFormats(false))
                {
                    var value = current.GetData(format, false);
                    if (value is Image image) { value = new Bitmap(image); resources.Add((IDisposable)value); }
                    if (value is MemoryStream memory) { value = new MemoryStream(memory.ToArray()); resources.Add((IDisposable)value); }
                    if (value is not null) previous.SetData(format, false, value);
                }

            uint? writtenSequence = null;
            try
            {
                Clipboard.SetDataObject(data, true, 10, 75);
                writtenSequence = GetClipboardSequenceNumber();
                commit();
            }
            catch
            {
                // If durable completion fails, restore the snapshot unless another app changed the clipboard.
                if (writtenSequence is { } sequence && GetClipboardSequenceNumber() == sequence)
                    Clipboard.SetDataObject(previous, true, 10, 75);
                throw;
            }
        }
        catch (ExternalException) { throw new TransferError(503, "clipboard_busy", "Windows clipboard is busy. Close its current owner and retry the same transferId."); }
        finally { foreach (var resource in resources) resource.Dispose(); }
    }

    private static string BuildHtml(string fragment)
    {
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        const string template = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var start = Encoding.UTF8.GetByteCount(string.Format(template, 0, 0, 0, 0));
        var first = start + Encoding.UTF8.GetByteCount(prefix);
        var last = first + Encoding.UTF8.GetByteCount(fragment);
        return string.Format(template, start, last + Encoding.UTF8.GetByteCount(suffix), first, last) + prefix + fragment + suffix;
    }
}
