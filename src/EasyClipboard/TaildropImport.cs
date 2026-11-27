using System.Drawing.Imaging;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace EasyClipboard;

internal sealed class TaildropImport(HistoryBank bank, TransferService transfers, string? downloads = null)
{
    private const string Prefix = "EasyClipboard-temp-";
    internal static string ManagedFolder(string root) => Path.Combine(root, "taildrop-temp");
    private readonly string _managed = ManagedFolder(bank.Root);
    private readonly string _downloads = downloads ?? FindDownloads();

    private static string FindDownloads()
    {
        using var folders = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        return folders?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static IEnumerable<FileInfo> Candidates(string folder) =>
        Directory.Exists(folder) && !File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)
            ? new DirectoryInfo(folder).EnumerateFiles(Prefix + "*")
            : [];

    internal static IEnumerable<(Guid Id, FileInfo File)> ManagedImages(string root)
    {
        foreach (var file in Candidates(ManagedFolder(root)))
            if (TryGetId(file, out var id)) yield return (id, file);
    }

    private static bool TryGetId(FileInfo file, out Guid id)
    {
        id = Guid.Empty;
        var name = Path.GetFileNameWithoutExtension(file.Name);
        return !file.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
            file.Extension.ToLowerInvariant() is (".png" or ".jpeg" or ".jpg") &&
            name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(name.AsSpan(Prefix.Length), "D", out id) && id != Guid.Empty;
    }

    public async Task<(bool Expired, string? Warning)> RunAsync(bool enabled)
    {
        var now = DateTime.UtcNow;
        var expired = false;
        var retainedIds = bank.ListEntries().Select(entry => entry.Id).ToHashSet();
        foreach (var (id, file) in ManagedImages(bank.Root))
        {
            if (retainedIds.Contains(id)) continue;
            file.Delete();
            expired = true;
        }

        if (!enabled || transfers.Paused) return (expired, null);
        string? warning = null;
        foreach (var file in Candidates(_downloads).OrderBy(file => file.LastWriteTimeUtc))
        {
            if (!TryGetId(file, out var id) || now - file.LastWriteTimeUtc < TimeSpan.FromSeconds(3)) continue;
            var destination = Path.Combine(_managed, file.Name);
            if (File.Exists(destination)) continue;
            try
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Delete);
                if (Directory.Exists(_managed) && File.GetAttributes(_managed).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Temporary folder is a reparse point.");
                LocalFiles.CreatePrivateDirectory(_managed);
                await transfers.ReceiveAsync(() => ReadImage(stream, file.Extension, id));
                File.Move(file.FullName, destination);
                bank.TryPrune(id);
            }
            catch (TransferError error) when (error.Code is "busy" or "paused") { break; }
            catch (Exception error) when (error is TransferError or IOException or UnauthorizedAccessException)
            { warning = "A tagged Taildrop image could not be imported. Check the file in Downloads."; }
        }
        return (expired, warning);
    }

    private static ClipItem ReadImage(FileStream stream, string extension, Guid id)
    {
        if (stream.Length > Math.Min(Array.MaxLength, ImageResources.UploadBodyBudget))
            throw ImageResources.InsufficientMemory();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var format = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Png : ImageFormat.Jpeg;
        var png = TransferValidation.NormalizeImage(bytes, format);
        var hash = "taildrop:" + Convert.ToHexString(SHA256.HashData(bytes));
        return new ClipItem(id, DateTimeOffset.UtcNow, "image/png", null, png, null, hash);
    }
}
