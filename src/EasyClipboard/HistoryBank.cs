namespace EasyClipboard;

public sealed class HistoryBank
{
    private readonly object _gate = new();
    public string Root { get; }
    private string Folder => Path.Combine(Root, "history");
    public Preferences Preferences { get; private set; }
    public int UnreadableFiles { get; private set; }
    public bool MaintenanceWarning { get; private set; }

    public HistoryBank(string root)
    {
        Root = root;
        LocalFiles.CreatePrivateDirectory(root);
        LocalFiles.CreatePrivateDirectory(Folder);
        var settings = Path.Combine(root, "preferences.json");
        Preferences = File.Exists(settings) ? System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllBytes(settings), LocalFiles.Json)! : new();
        Preferences.Validate();
        Prune();
    }
    private string PathFor(Guid id) => Path.Combine(Folder, $"{id:N}.dat");
    public ClipItem? Find(Guid id)
    {
        lock (_gate) return File.Exists(PathFor(id)) ? LocalFiles.LoadSecret<ClipItem>(PathFor(id)) : null;
    }
    public void Save(ClipItem item)
    {
        lock (_gate) LocalFiles.SaveSecret(PathFor(item.Id), item);
    }
    public List<ClipItem> List(bool includePending = false)
    {
        lock (_gate)
        {
            var list = new List<ClipItem>();
            UnreadableFiles = 0;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.dat"))
            {
                try
                {
                    var item = LocalFiles.LoadSecret<ClipItem>(file);
                    if (includePending || item.Applied) list.Add(item);
                }
                catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
                { UnreadableFiles++; }
            }
            return list.OrderByDescending(x => x.ReceivedAt).ToList();
        }
    }
    public void Delete(Guid id) { lock (_gate) File.Delete(PathFor(id)); }
    public void Clear()
    {
        lock (_gate) foreach (var file in Directory.EnumerateFiles(Folder, "*.dat")) File.Delete(file);
    }
    public void Configure(Preferences preferences)
    {
        preferences.Validate();
        lock (_gate)
        {
            LocalFiles.Write(Path.Combine(Root, "preferences.json"), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(preferences, LocalFiles.Json));
            Preferences = preferences;
            Prune();
        }
    }
    public void Prune(Guid? preserve = null)
    {
        lock (_gate)
        {
            var count = 0;
            long bytes = 0;
            foreach (var item in List(true))
            {
                var file = PathFor(item.Id);
                bytes += new FileInfo(file).Length;
                count++;
                if (item.Id != preserve && (item.ReceivedAt < DateTimeOffset.UtcNow.AddDays(-Preferences.RetentionDays) ||
                    count > Preferences.MaxItems || bytes > Preferences.MaxMegabytes * 1024L * 1024L)) File.Delete(file);
            }
            foreach (var temporary in Directory.EnumerateFiles(Folder, "*.tmp")) File.Delete(temporary);
        }
    }
    public void TryPrune(Guid? preserve = null)
    {
        try { Prune(preserve); MaintenanceWarning = false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { MaintenanceWarning = true; }
    }
}
