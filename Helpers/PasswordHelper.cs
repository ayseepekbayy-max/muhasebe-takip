using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Helpers;

public enum PasswordStorageFormat
{
    Invalid,
    NewHash,
    LegacySha256,
    LegacyPlainText
}

public readonly record struct PasswordCheckResult(
    bool Succeeded,
    bool RehashNeeded,
    PasswordStorageFormat Format);

public static class PasswordHelper
{
    private static readonly PasswordHasher<Kullanici> Hasher = new();

    public static string Hash(Kullanici kullanici, string password)
    {
        ArgumentNullException.ThrowIfNull(kullanici);
        ArgumentException.ThrowIfNullOrEmpty(password);

        return Hasher.HashPassword(kullanici, password);
    }

    public static PasswordCheckResult Verify(Kullanici kullanici, string password, string storedPassword)
    {
        ArgumentNullException.ThrowIfNull(kullanici);

        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedPassword))
            return Failed(PasswordStorageFormat.Invalid);

        var format = DetectFormat(storedPassword);
        switch (format)
        {
            case PasswordStorageFormat.NewHash:
                var identityResult = Hasher.VerifyHashedPassword(kullanici, storedPassword, password);
                return identityResult switch
                {
                    PasswordVerificationResult.Success => new(true, false, format),
                    PasswordVerificationResult.SuccessRehashNeeded => new(true, true, format),
                    _ => Failed(format)
                };

            case PasswordStorageFormat.LegacySha256:
                var expectedHash = Convert.FromBase64String(storedPassword);
                var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
                return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash)
                    ? new(true, true, format)
                    : Failed(format);

            case PasswordStorageFormat.LegacyPlainText:
                var storedBytes = Encoding.UTF8.GetBytes(storedPassword);
                var candidateBytes = Encoding.UTF8.GetBytes(password);
                var matches = storedBytes.Length == candidateBytes.Length &&
                              CryptographicOperations.FixedTimeEquals(storedBytes, candidateBytes);
                return matches ? new(true, true, format) : Failed(format);

            default:
                return Failed(format);
        }
    }

    public static PasswordStorageFormat DetectFormat(string? storedPassword)
    {
        if (string.IsNullOrEmpty(storedPassword))
            return PasswordStorageFormat.Invalid;

        try
        {
            var decoded = Convert.FromBase64String(storedPassword);

            if (decoded.Length == SHA256.HashSizeInBytes)
                return PasswordStorageFormat.LegacySha256;

            if (decoded.Length > SHA256.HashSizeInBytes && decoded[0] is 0x00 or 0x01)
                return PasswordStorageFormat.NewHash;
        }
        catch (FormatException)
        {
            // Base64 olmayan eski kayıtlar yalnız legacy düz metin uyumluluğu için ele alınır.
        }

        return PasswordStorageFormat.LegacyPlainText;
    }

    private static PasswordCheckResult Failed(PasswordStorageFormat format) => new(false, false, format);
}
