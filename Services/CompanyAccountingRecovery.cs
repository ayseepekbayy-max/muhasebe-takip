using System.Security.Cryptography;

namespace MuhasebeTakip2.App.Services;

public sealed class CompanyAccountingRecovery(AccountingBackupService backup, CompanyRecoveryArchive archive)
{
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
