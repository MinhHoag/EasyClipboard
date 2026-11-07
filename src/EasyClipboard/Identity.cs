using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace EasyClipboard;

public sealed record PairingSecrets(string Ssid, string WifiPassword, string Token, byte[] AuthorityPfx);

public sealed class Identity : IDisposable
{
    private readonly string _path;
    private PairingSecrets _secrets;
    private readonly object _gate = new();
    public X509Certificate2 Authority { get; }
    public string Ssid => _secrets.Ssid;
    public string WifiPassword => _secrets.WifiPassword;
    public string Token { get { lock (_gate) return _secrets.Token; } }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Authority.RawData));

    public Identity(string root)
    {
        LocalFiles.CreatePrivateDirectory(root);
        _path = Path.Combine(root, "identity.dat");
        if (File.Exists(_path)) _secrets = LocalFiles.LoadSecret<PairingSecrets>(_path);
        else
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest($"CN=EasyClipboard {Guid.NewGuid():N}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            _secrets = new PairingSecrets($"EasyClipboard-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}",
                Convert.ToHexString(RandomNumberGenerator.GetBytes(12)), NewToken(), certificate.Export(X509ContentType.Pfx));
            LocalFiles.SaveSecret(_path, _secrets);
        }
        Authority = X509CertificateLoader.LoadPkcs12(_secrets.AuthorityPfx, null, X509KeyStorageFlags.EphemeralKeySet);
        if (Authority.NotAfter <= DateTime.Now.AddDays(2)) throw new InvalidOperationException("The local authority has expired. Remove the old iPhone profile, then reset the local identity to pair again.");
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public void RotateToken()
    {
        lock (_gate)
        {
            var updated = _secrets with { Token = NewToken() };
            LocalFiles.SaveSecret(_path, updated);
            _secrets = updated;
        }
    }

    public bool Authorizes(string header) => AuthenticationFailure(header) is null;

    public TransferError? AuthenticationFailure(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return new(401, "authorization_missing", "The request arrived without an Authorization header. Check Headers in the POST action for the active text/image branch.");
        if (!AuthenticationHeaderValue.TryParse(header.Trim(), out var parsed) ||
            !parsed.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(parsed.Parameter))
            return new(401, "authorization_format", "The Authorization header was received, but it must contain Bearer followed by the pairing key.");

        var token = parsed.Parameter.Trim();
        if (token.Length != 64)
            return new(401, "pairing_key_length", $"The Authorization header was received with {token.Length} key characters; this receiver expects 64. Check for extra Bearer words, spaces, or duplicate headers.");
        byte[] supplied;
        try { supplied = Convert.FromHexString(token); }
        catch (FormatException)
        { return new(401, "pairing_key_format", "The received key has 64 characters but contains a non-hexadecimal character. The pairing key uses only 0–9 and A–F."); }
        // Hex case and ordinary HTTP scheme/outer whitespace are presentation, not key material.
        return CryptographicOperations.FixedTimeEquals(supplied, Convert.FromHexString(Token)) ? null
            : new(401, "pairing_key_mismatch", "The Authorization header and key format are valid, but the key does not match this running receiver's pairing key.");
    }

    public X509Certificate2 IssueServer(IPAddress address)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=EasyClipboard receiver", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        var expires = DateTimeOffset.UtcNow.AddDays(90);
        if (expires > Authority.NotAfter.ToUniversalTime()) expires = Authority.NotAfter.ToUniversalTime();
        using var certificate = request.Create(Authority, DateTimeOffset.UtcNow.AddMinutes(-5), expires, RandomNumberGenerator.GetBytes(16));
        using var withKey = certificate.CopyWithPrivateKey(key);
        // Schannel on Windows needs a provider-backed key imported from PFX. Do not add the
        // certificate to a trust store or use PersistKeySet; disposal releases the temporary key.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
    }

    public byte[] Profile()
    {
        static XElement Pair(string key, string value) => new("key", key);
        var id = new Guid(SHA256.HashData(Authority.RawData).AsSpan(0, 16)).ToString();
        var payload = new XElement("dict",
            Pair("PayloadType", ""), new XElement("string", "com.apple.security.root"),
            Pair("PayloadVersion", ""), new XElement("integer", 1),
            Pair("PayloadIdentifier", ""), new XElement("string", $"local.easyclipboard.root.{id}"),
            Pair("PayloadUUID", ""), new XElement("string", id),
            Pair("PayloadDisplayName", ""), new XElement("string", Authority.GetNameInfo(X509NameType.SimpleName, false)),
            Pair("PayloadContent", ""), new XElement("data", Convert.ToBase64String(Authority.RawData)));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                Pair("PayloadType", ""), new XElement("string", "Configuration"),
                Pair("PayloadVersion", ""), new XElement("integer", 1),
                Pair("PayloadIdentifier", ""), new XElement("string", $"local.easyclipboard.profile.{id}"),
                Pair("PayloadUUID", ""), new XElement("string", new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(id)).AsSpan(0, 16)).ToString()),
                Pair("PayloadDisplayName", ""), new XElement("string", "EasyClipboard local certificate"),
                Pair("PayloadContent", ""), new XElement("array", payload))));
        return Encoding.UTF8.GetBytes(document.ToString());
    }

    public void Dispose() => Authority.Dispose();
}
