using System.Security.Cryptography;

namespace MuhasebeTakip2.App.Services;

/// <summary>Processes one durable cleanup entry in a private, immutable-object store.</summary>
public sealed class CompanyFileCleanup(string objects, string quarantine)
{
    public enum Result { Preserved, Complete }

    public sealed record Entry(int CompanyId, Guid OperationId, Guid StorageId, string Sha256);
    public sealed record Ownership(int OwnerCompanyId, bool HasOtherReferences, bool IsRetired);

    // Call under the same persistent per-object lock used by reference writers and workers.
    // Retirement and the cleanup entry must have committed before this method can run.
    public Result Process(Entry entry, Ownership? ownership, string? verifiedObjectPath = null)
    {
        if (entry.CompanyId <= 1 || entry.OperationId == Guid.Empty || entry.StorageId == Guid.Empty)
            throw new ArgumentException("Invalid cleanup entry.", nameof(entry));
        if (ownership is null || ownership.OwnerCompanyId != entry.CompanyId ||
            ownership.HasOtherReferences || !ownership.IsRetired)
            return Result.Preserved;

        var original = verifiedObjectPath ?? Path.Combine(objects, entry.StorageId.ToString("N"));
        if (!Path.GetFullPath(original).StartsWith(Path.GetFullPath(objects).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Cleanup object is outside its store.");
        var staged = Path.Combine(quarantine, $"{entry.OperationId:N}-{entry.StorageId:N}");
        var expected = Convert.FromHexString(entry.Sha256);
        if (expected.Length != 32)
            throw new InvalidDataException("Invalid document checksum.");

        var live = ExistsWithoutLink(original);
        var pending = ExistsWithoutLink(staged);
        if (live && pending)
            throw new IOException("Both source and quarantine object exist.");
        if (live)
        {
            Verify(original, expected);
            File.Move(original, staged);
            pending = true;
        }
        if (pending)
        {
            Verify(staged, expected);
            File.Delete(staged);
        }
        return Result.Complete;
    }

    private static bool ExistsWithoutLink(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Cleanup path is not a regular object.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
    }

    private static void Verify(string path, byte[] expected)
    {
        using var stream = File.OpenRead(path);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), expected))
            throw new InvalidDataException("Cleanup object differs from the archived document.");
    }
}
