using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MuhasebeTakip2.App.Services;

public sealed class CompanyRecoveryKeySource(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string RenderSecretDirectory = "/etc/secrets";
    private static readonly Regex RenderGeneration = new(
        @"\A\.\.[0-9]{4}_[0-9]{2}_[0-9]{2}_[0-9]{2}_[0-9]{2}_[0-9]{2}\.[0-9]{1,10}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public CompanyRecoveryArchive? OpenArchive(string? requestedVersion = null)
    {
        if (!configuration.GetValue<bool>("CompanyRecovery:Enabled"))
            return null;
        var version = requestedVersion ?? configuration["CompanyRecovery:KeyVersion"];
        if (!IsVersion(version))
            return null;
        var key = ReadKey(RenderSecretDirectory, version!);
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
            var path = ResolveKeyPath(secretDirectory, version);
            if (path is null || new FileInfo(path).Length is < 44 or > 46)
                return null;
            using var input = File.OpenRead(path);
            var length = input.ReadAtLeast(encoded, encoded.Length, false);
            var data = encoded[..length];
            if (data.EndsWith("\r\n"u8))
                data = data[..^2];
            else if (data.EndsWith("\n"u8))
                data = data[..^1];
            if (data.Length != 44 || data[^1] != '=')
                return null;
            foreach (var b in data[..^1])
                if (b is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
                    >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/'))
                    return null;
            var key = new byte[32];
            if (System.Buffers.Text.Base64.DecodeFromUtf8(data, key, out var consumed, out var written) ==
                System.Buffers.OperationStatus.Done && consumed == data.Length && written == key.Length)
            {
                Span<byte> canonical = stackalloc byte[44];
                System.Buffers.Text.Base64.EncodeToUtf8(key, canonical, out _, out _);
                var valid = canonical.SequenceEqual(data);
                CryptographicOperations.ZeroMemory(canonical);
                if (valid) return key;
            }
            CryptographicOperations.ZeroMemory(key);
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }

    private static string? ResolveKeyPath(string directory, string version)
    {
        var renderPath = OperatingSystem.IsLinux() && directory == RenderSecretDirectory;
        if (renderPath && (!IsReadOnlySecretMount() || IsLink("/etc") || IsLink(directory)))
            return null;

        var name = $"firmova-recovery-key-{version}";
        var path = Path.Combine(directory, name);
        if (IsLink(path))
        {
            // Render atomically selects a generation through ..data. Read only the
            // validated generation path, never follow general symbolic-link chains.
            if (!renderPath || version != "1" || new FileInfo(path).LinkTarget != $"..data/{name}")
                return null;
            var generation = new DirectoryInfo(Path.Combine(directory, "..data")).LinkTarget;
            if (generation is null || !RenderGeneration.IsMatch(generation))
                return null;
            var generationPath = Path.Combine(directory, generation);
            var attributes = File.GetAttributes(generationPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                return null;
            path = Path.Combine(generationPath, name);
        }

        return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 ? path : null;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsReadOnlySecretMount()
    {
        var found = false;
        foreach (var line in File.ReadLines("/proc/self/mountinfo"))
        {
            var fields = line.Split(' ', 7);
            if (fields.Length < 7)
                continue;
            if (fields[4].StartsWith(RenderSecretDirectory + "/", StringComparison.Ordinal))
                return false;
            if (fields[4] != RenderSecretDirectory)
                continue;
            if (found || !fields[5].Split(',').Contains("ro", StringComparer.Ordinal))
                return false;
            found = true;
        }
        return found;
    }
}
