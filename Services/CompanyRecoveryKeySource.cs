using System.Security.Cryptography;

namespace MuhasebeTakip2.App.Services;

public sealed class CompanyRecoveryKeySource(IConfiguration configuration, IWebHostEnvironment environment)
{
    public CompanyRecoveryArchive? OpenArchive()
    {
        var version = configuration["CompanyRecovery:KeyVersion"];
        if (!IsVersion(version))
            return null;
        var key = ReadKey("/etc/secrets", version!);
        return key is null ? null : CompanyRecoveryArchive.OnCompanyDisk(environment, key, version);
    }

    internal static bool IsVersion(string? version) =>
        version is { Length: > 0 and <= 32 } && version.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    internal static byte[]? ReadKey(string secretDirectory, string version)
    {
        if (!IsVersion(version))
            return null;
        Span<byte> encoded = stackalloc byte[47];
        try
        {
            var path = Path.Combine(secretDirectory, $"firmova-recovery-key-{version}");
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return null;
            using var input = File.OpenRead(path);
            var length = input.ReadAtLeast(encoded, encoded.Length, false);
            var data = encoded[..length];
            if (data.EndsWith("\n"u8))
                data = data[..^1];
            if (data.EndsWith("\r"u8))
                data = data[..^1];
            if (data.Length != 44)
                return null;
            var key = new byte[32];
            if (System.Buffers.Text.Base64.DecodeFromUtf8(data, key, out var consumed, out var written) ==
                System.Buffers.OperationStatus.Done && consumed == data.Length && written == key.Length)
                return key;
            CryptographicOperations.ZeroMemory(key);
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }
}
