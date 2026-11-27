using System.Security.Cryptography;

namespace EasyClipboard;

internal sealed record DeveloperPassword(byte[] Salt, byte[] Hash)
{
    private const int Iterations = 600_000;

    public static DeveloperPassword Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new(salt, Derive(password, salt));
    }

    public bool Matches(string password) =>
        CryptographicOperations.FixedTimeEquals(Hash, Derive(password, Salt));

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}
