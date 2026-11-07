using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace EasyClipboard;

public static class LocalFiles
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyClipboard");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void CreatePrivateDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    public static void Write(string path, byte[] data)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void SaveSecret<T>(string path, T value) => Write(path,
        ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value, Json), null, DataProtectionScope.CurrentUser));
    public static T LoadSecret<T>(string path) => JsonSerializer.Deserialize<T>(
        ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser), Json)!;
}

public sealed record Preferences(int RetentionDays = 30, int MaxItems = 200, int MaxMegabytes = 256)
{
    public void Validate()
    {
        if (RetentionDays is < 1 or > 3650 || MaxItems is < 1 or > 10000 || MaxMegabytes is < 32 or > 4096)
            throw new ArgumentException("Use 1–3650 days, 1–10000 items, and 32–4096 MB.");
    }
}
