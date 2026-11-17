using System.Security.Cryptography;
using System.Text.Json;

namespace MuhasebeTakip2.App.Services;

/// <summary>Durable encrypted storage for a prepared tenant recovery snapshot.</summary>
public sealed class CompanyRecoveryArchive(string directory, byte[] key, string? keyVersion = null)
{
    private const long DiskReserveBytes = 64L * 1024 * 1024;
    private const int EnvelopeBytes = 28;

    public static CompanyRecoveryArchive OnCompanyDisk(IWebHostEnvironment environment, byte[] key, string? keyVersion = null) =>
        new(CompanyDiskStorage.Archives(environment), key, keyVersion);

    internal static bool HasCapacity(long availableBytes, long plaintextBytes) =>
        plaintextBytes >= 0 && availableBytes >= DiskReserveBytes + EnvelopeBytes &&
        plaintextBytes <= availableBytes - DiskReserveBytes - EnvelopeBytes;

    public sealed record Document(Guid StorageId, byte[] Content);
    public sealed record Snapshot(int CompanyId, Guid OperationId,
        Dictionary<string, JsonElement[]> Tables, Document[] Documents);

    public void Save(Snapshot snapshot)
    {
        ValidateIdentity(snapshot.CompanyId, snapshot.OperationId);
        var destination = PathFor(snapshot.CompanyId, snapshot.OperationId);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        try
        {
            CompanyDiskStorage.EnsurePrivateDirectory(directory);
            var fullPath = Path.GetFullPath(directory);
            var volume = DriveInfo.GetDrives()
                .Where(drive => Path.EndsInDirectorySeparator(drive.RootDirectory.FullName)
                    ? fullPath.StartsWith(drive.RootDirectory.FullName, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    : fullPath == drive.RootDirectory.FullName ||
                        fullPath.StartsWith(drive.RootDirectory.FullName + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .OrderByDescending(drive => drive.RootDirectory.FullName.Length).FirstOrDefault();
            if (volume is null || !HasCapacity(volume.AvailableFreeSpace, plaintext.LongLength))
                throw new IOException("Firma yedeği için kalıcı diskte yeterli boş alan yok.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var cipher = new AesGcm(key, tag.Length);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, Identity(snapshot.CompanyId, snapshot.OperationId));
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(nonce);
                stream.Write(tag);
                stream.Write(ciphertext);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            File.Delete(temporary);
        }
    }

    public Snapshot Read(int companyId, Guid operationId)
    {
        ValidateIdentity(companyId, operationId);
        var bytes = File.ReadAllBytes(PathFor(companyId, operationId));
        return Decode(companyId, operationId, bytes);
    }

    public (string Name, byte[] Bytes) ReadEncryptedDownload(int companyId, Guid operationId)
    {
        ValidateIdentity(companyId, operationId);
        var path = PathFor(companyId, operationId);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Archive is not a regular file.");
        var bytes = File.ReadAllBytes(path);
        _ = Decode(companyId, operationId, bytes);
        return (Path.GetFileName(path), bytes);
    }

    internal Snapshot ReadVerified(int companyId, Guid operationId, string digest)
    {
        var (_, bytes) = ReadEncryptedDownload(companyId, operationId);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != digest)
            throw new InvalidDataException("Encrypted archive digest changed.");
        return Decode(companyId, operationId, bytes);
    }

    internal void Consume(int companyId, Guid operationId)
    {
        ValidateIdentity(companyId, operationId);
        using var marker = new FileStream(PathFor(companyId, operationId) + ".used", FileMode.CreateNew);
        marker.Flush(flushToDisk: true);
    }

    private Snapshot Decode(int companyId, Guid operationId, byte[] bytes)
    {
        if (bytes.Length <= 28)
            throw new InvalidDataException("Incomplete recovery archive.");
        var plaintext = new byte[bytes.Length - 28];
        try
        {
            using var cipher = new AesGcm(key, 16);
            cipher.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16),
                plaintext, Identity(companyId, operationId));
            var snapshot = JsonSerializer.Deserialize<Snapshot>(plaintext);
            if (snapshot is null || snapshot.CompanyId != companyId || snapshot.OperationId != operationId)
                throw new InvalidDataException("Recovery archive identity mismatch.");
            return snapshot;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private string PathFor(int companyId, Guid operationId)
    {
        if (keyVersion is not null && !CompanyRecoveryKeySource.IsVersion(keyVersion))
            throw new InvalidDataException("Invalid recovery key version.");
        var suffix = keyVersion is null ? "" : $"-key-{keyVersion}";
        return Path.Combine(directory, FormattableString.Invariant($"{companyId}-{operationId:N}{suffix}.firmova-recovery"));
    }

    private byte[] Identity(int companyId, Guid operationId) =>
        System.Text.Encoding.UTF8.GetBytes(keyVersion is null
            ? FormattableString.Invariant($"firmova-recovery-v1/{companyId}/{operationId:N}")
            : FormattableString.Invariant($"firmova-recovery-v2/{companyId}/{operationId:N}/{keyVersion}"));

    private static void ValidateIdentity(int companyId, Guid operationId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(companyId, 2);
        if (operationId == Guid.Empty)
            throw new ArgumentException("A recovery operation ID is required.", nameof(operationId));
    }
}
