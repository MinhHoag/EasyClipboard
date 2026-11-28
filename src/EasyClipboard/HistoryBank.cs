namespace EasyClipboard;

public sealed record HistoryEntry(Guid Id, DateTimeOffset ReceivedAt, string Label, bool Applied)
{
    public bool IsImage { get; private init; }

    public override string ToString() => $"{Group}, {When}   {Label}";

    // List group, like File Explorer: Today, Yesterday, Earlier this week, Last week, Earlier this month, ...
    public string Group
    {
        get
        {
            var day = ReceivedAt.LocalDateTime.Date;
            var today = DateTime.Today;
            var week = today.AddDays(-(int)today.DayOfWeek); // weeks start Sunday, like the calendar
            var month = new DateTime(today.Year, today.Month, 1);
            return day >= today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : day >= week ? "Earlier this week"
                : day >= week.AddDays(-7) ? "Last week" : day >= month ? "Earlier this month" : day >= month.AddMonths(-1) ? "Last month"
                : day.Year == today.Year ? "Earlier this year" : "Older";
        }
    }

    // Date and time under its group: just the time for Today and Yesterday, the weekday this week and last.
    public string When
    {
        get
        {
            var local = ReceivedAt.LocalDateTime;
            var time = local.ToString("t");
            return Group switch
            {
                "Today" or "Yesterday" => time,
                "Earlier this week" or "Last week" => $"{local:ddd} {time}",
                "Older" => $"{local:d} {time}",
                _ => $"{local:MMM d} {time}"
            };
        }
    }

    internal static HistoryEntry Create(Guid id, DateTimeOffset receivedAt, string? text, bool applied) =>
        new(id, receivedAt, text is null ? "Photo / screenshot" : text[..Math.Min(70, text.Length)].Replace('\r', ' ').Replace('\n', ' '), applied)
        { IsImage = text is null };
}

public sealed class HistoryBank
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HistoryEntry> _entries = new();
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    // Older history uses the same encrypted JSON format. Only deserialize the fields
    // needed for the list, skipping the base64 PNG and HTML fields.
    private sealed record StoredSummary(Guid Id, DateTimeOffset ReceivedAt, string? Text, bool Applied);
    public string Root { get; }
    private string Folder => Path.Combine(Root, "history");
    public Preferences Preferences { get; private set; }
    public int UnreadableFiles { get { lock (_gate) return _unreadable.Count; } }
    public bool MaintenanceWarning { get; private set; }

    public HistoryBank(string root)
    {
        Root = root;
        LocalFiles.CreatePrivateDirectory(root);
        LocalFiles.CreatePrivateDirectory(Folder);
        var settings = Path.Combine(root, "preferences.json");
        Preferences = File.Exists(settings) ? System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllBytes(settings), LocalFiles.Json)! : new();
        Preferences.Validate();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.dat"))
        {
            try
            {
                var item = LocalFiles.LoadSecret<StoredSummary>(file);
                if (item is null || !string.Equals(PathFor(item.Id), file, StringComparison.OrdinalIgnoreCase))
                    throw new System.Text.Json.JsonException();
                _entries.Add(item.Id, HistoryEntry.Create(item.Id, item.ReceivedAt, item.Text, item.Applied));
            }
            catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            { _unreadable.Add(file); }
        }
        Prune();
    }
    private string PathFor(Guid id) => Path.Combine(Folder, $"{id:N}.dat");
    public ClipItem? Find(Guid id)
    {
        lock (_gate) return File.Exists(PathFor(id)) ? LocalFiles.LoadSecret<ClipItem>(PathFor(id)) : null;
    }
    public void Save(ClipItem item)
    {
        lock (_gate)
        {
            var path = PathFor(item.Id);
            LocalFiles.SaveSecret(path, item);
            _entries[item.Id] = HistoryEntry.Create(item.Id, item.ReceivedAt, item.Text, item.Applied);
            _unreadable.Remove(path);
        }
    }
    public List<HistoryEntry> ListEntries(bool includePending = false)
    {
        lock (_gate) return _entries.Values.Where(x => includePending || x.Applied).OrderByDescending(x => x.ReceivedAt).ToList();
    }

    public List<ClipItem> List(bool includePending = false)
    {
        lock (_gate)
        {
            var list = new List<ClipItem>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.dat"))
            {
                try
                {
                    var item = LocalFiles.LoadSecret<ClipItem>(file);
                    if (includePending || item.Applied) list.Add(item);
                }
                catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
                { _unreadable.Add(file); }
            }
            return list.OrderByDescending(x => x.ReceivedAt).ToList();
        }
    }
    public void Delete(Guid id)
    {
        lock (_gate)
        {
            var path = PathFor(id);
            File.Delete(path);
            _entries.Remove(id);
            _unreadable.Remove(path);
        }
    }

    public int ClearDuplicates()
    {
        lock (_gate)
        {
            // The transfer hash includes the request ID. Compare the saved content instead,
            // loading just one payload at a time so a large history stays manageable.
            var seen = new HashSet<(string Type, string? Text, string? Html, string? Png)>();
            var removed = 0;
            foreach (var entry in ListEntries())
            {
                ClipItem? item;
                try { item = Find(entry.Id); }
                catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
                {
                    _unreadable.Add(PathFor(entry.Id));
                    continue;
                }
                if (item is not { Applied: true }) continue;

                var key = (item.ContentType, TextDigest(item.Text), TextDigest(item.Html),
                    item.Png is { } png ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)) : null);
                if (seen.Add(key)) continue;
                Delete(entry.Id);
                removed++;
            }
            return removed;
        }
    }

    private static string? TextDigest(string? text) => text is null ? null :
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(text.AsSpan())));

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var file in Directory.EnumerateFiles(Folder, "*.dat"))
            {
                File.Delete(file);
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) _entries.Remove(id);
                _unreadable.Remove(file);
            }
        }
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
            var originals = TaildropImport.ManagedImages(Root)
                .ToLookup(image => image.Id, image => image.File);
            foreach (var item in ListEntries(true))
            {
                var file = new FileInfo(PathFor(item.Id));
                if (!file.Exists) { _entries.Remove(item.Id); continue; }
                bytes += file.Length;
                bytes += originals[item.Id].Where(original => original.Exists).Sum(original => original.Length);
                count++;
                if (item.Id != preserve && (item.ReceivedAt < DateTimeOffset.UtcNow.AddDays(-Preferences.RetentionDays) ||
                    count > Preferences.MaxItems || bytes > Preferences.MaxMegabytes * 1024L * 1024L)) Delete(item.Id);
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
