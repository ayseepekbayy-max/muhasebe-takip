using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MuhasebeTakip2.App.Services;

public sealed class CompanyAccountingRecovery(AccountingBackupService backup, CompanyRecoveryArchive archive)
{
    public static bool TryResolveUploadIdentity(string? name, Guid? suppliedOperation, string? suppliedVersion,
        out Guid operation, out string version)
    {
        operation = Guid.Empty;
        version = "";
        if (name is null || name.Length > 128 || name.IndexOfAny(['/', '\\', ':']) >= 0 ||
            !name.EndsWith(".firmova-recovery", StringComparison.OrdinalIgnoreCase)) return false;
        var named = TryReadDownloadName(name, out var namedOperation, out var namedVersion);
        if (!suppliedOperation.HasValue && string.IsNullOrEmpty(suppliedVersion))
        { operation = namedOperation; version = namedVersion; return named; }
        if (suppliedOperation is null || suppliedOperation == Guid.Empty || !CompanyRecoveryKeySource.IsVersion(suppliedVersion))
            return false;
        if (named && (namedOperation != suppliedOperation || namedVersion != suppliedVersion)) return false;
        operation = suppliedOperation.Value;
        version = suppliedVersion!;
        return true; // Still untrusted until tenant-bound AES-GCM authentication succeeds.
    }

    public static bool TryReadDownloadName(string? name, out Guid operation, out string version)
    {
        operation = Guid.Empty;
        version = "";
        if (name is null || name.Length > 128) return false;
        var match = Regex.Match(name, @"\Afirmova-([0-9a-fA-F]{32})-key-([a-zA-Z0-9-]{1,32})\.firmova-recovery\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (!match.Success || !Guid.TryParseExact(match.Groups[1].Value, "N", out operation) || operation == Guid.Empty)
            return false;
        version = match.Groups[2].Value;
        return CompanyRecoveryKeySource.IsVersion(version);
    }

    public async Task<AccountingBackupService.RestoreResult> RestoreUploadedAsync(
        int companyId, Guid operation, Stream input, long length, CancellationToken ct)
    {
        if (length is <= 28 or > CompanyRecoveryArchive.MaxAccountingEnvelopeBytes)
            throw new InvalidDataException("Invalid accounting archive size.");
        var envelope = new byte[checked((int)length)];
        byte[]? data = null;
        try
        {
            await input.ReadExactlyAsync(envelope, ct);
            if (await input.ReadAsync(new byte[1], ct) != 0)
                throw new InvalidDataException("Archive exceeds declared size.");
            ct.ThrowIfCancellationRequested();
            data = archive.ReadUploadedAccountingPackage(companyId, operation, envelope);
            // Compressed JSON may require much more RAM than the uploaded envelope.
            using (var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read))
            {
                var manifest = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing manifest.");
                if (!AccountingRecoveryAdmission.HasRestoreMemoryBudget(data.LongLength, manifest.Length))
                    throw new AccountingBackupService.BackupException("Geri yükleme için yeterli güvenli bellek bütçesi yok. Daha küçük bir arşiv veya daha fazla bellek gerekiyor.");
            }
            using var stream = new MemoryStream(data, writable: false);
            return await backup.RestoreAsync(stream, companyId, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
            if (data is not null) CryptographicOperations.ZeroMemory(data);
        }
    }

    public async Task<Guid> CreateAsync(int companyId, CancellationToken ct)
    {
        var data = await backup.ExportAsync(companyId, ct, requireUniqueDocuments: true);
        var operation = Guid.NewGuid();
        try
        {
            ct.ThrowIfCancellationRequested();
            archive.SaveAccountingPackage(companyId, operation, data);
            return operation;
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public async Task<AccountingBackupService.RestoreResult> RestoreAsync(int companyId, Guid operation, CancellationToken ct)
    {
        var data = archive.ReadAccountingPackage(companyId, operation);
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            return await backup.RestoreAsync(stream, companyId, ct);
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
}
