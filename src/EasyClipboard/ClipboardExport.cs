using System.Text;

namespace EasyClipboard;

public sealed record ClipboardSnapshot(string? Text = null, byte[]? Png = null);
public sealed record ClipboardDownload(string ContentType, byte[] Bytes);

public interface IClipboardSource
{
    Task<ClipboardSnapshot> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Reads on demand only while the user explicitly enables phone access.</summary>
public sealed class ClipboardExport(IClipboardSource source)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _enabled;
    private long _generation;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Interlocked.Increment(ref _generation); }
    }

    public async Task<ClipboardDownload> ReadAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Read(ref _generation);
        EnsureEnabled(generation);
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new TransferError(429, "clipboard_read_busy", "Another clipboard fetch is in progress. Try again.");
        try
        {
            EnsureEnabled(generation);
            var snapshot = await source.ReadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureEnabled(generation);
            if (snapshot.Png is null && string.IsNullOrEmpty(snapshot.Text))
                throw new TransferError(409, "clipboard_empty", "The PC clipboard is empty. Copy text or an image first.");
            // Same available-memory check and pixel-preserving sanitizer as phone-to-PC transfers.
            var item = TransferValidation.Prepare(new TransferRequest(1, Guid.NewGuid(),
                snapshot.Png is null ? "text/plain" : "image/png", snapshot.Text,
                snapshot.Png is null ? null : Convert.ToBase64String(snapshot.Png)));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureEnabled(generation);
            return item.Png is { } png ? new("image/png", png) : new("text/plain; charset=utf-8", Encoding.UTF8.GetBytes(item.Text!));
        }
        finally { _gate.Release(); }
    }

    private void EnsureEnabled(long generation)
    {
        if (!Enabled || generation != Interlocked.Read(ref _generation))
            throw new TransferError(403, "clipboard_export_disabled", "Turn on Settings → Phone fetch in EasyClipboard first.");
    }
}
