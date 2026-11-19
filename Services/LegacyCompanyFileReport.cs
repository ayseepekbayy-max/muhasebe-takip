using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public sealed class LegacyCompanyFileReport(AppDbContext db, IWebHostEnvironment environment)
{
    public sealed record Item(string Source, int RecordId, string Status);
    public sealed record Report(IReadOnlyList<Item> Items, int PrivateMessages, int PrivateMediaReferences,
        int PresenceRecords, int HiddenRecords, int StoredMedia)
    {
        public bool HasPrivatePanelData => PrivateMessages > 0 || PrivateMediaReferences > 0 ||
            PresenceRecords > 0 || HiddenRecords > 0 || StoredMedia > 0;
    }
    private sealed record Link(int Company, string Source, int Id, string Path, bool ParentMatches);

    private static readonly Regex LegacyPath = new(
        @"\A/uploads/(?<folder>cekler|cariler|faturalar)/[a-z0-9-]+\.(?:pdf|png|jpg|jpeg|webp|xlsx|xls|docx)\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<Report> InspectAsync(int company, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, ct);
        var checks = db.Cekler.Where(x => x.ResimYolu != null && x.ResimYolu != "")
            .Select(x => new Link(x.FirmaId, "Çek", x.Id, x.ResimYolu!, true));
        var attachments = db.EkDosyalar.Select(x => new Link(x.FirmaId,
            x.FaturaId != null ? "Fatura" : "Cari", x.Id, x.DosyaYolu,
            x.FaturaId != null && x.CariKartId == null && db.Faturalar.Any(p => p.Id == x.FaturaId && p.FirmaId == x.FirmaId) ||
            x.CariKartId != null && x.FaturaId == null && db.CariKartlar.Any(p => p.Id == x.CariKartId && p.FirmaId == x.FirmaId)));
        var logos = db.Firmalar.Where(x => x.LogoYolu != "" && !x.LogoYolu.StartsWith("data:"))
            .Select(x => new Link(x.Id, "Logo", x.Id, x.LogoYolu, false));
        var links = await checks.ToListAsync(ct);
        links.AddRange(await attachments.ToListAsync(ct));
        links.AddRange(await logos.ToListAsync(ct));
        var aliases = links.GroupBy(x => ComparisonPath(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var items = new List<Item>();
        var registered = db.Database.IsNpgsql()
            ? await (from reference in db.Set<CompanyStoredFileReference>()
                     join file in db.Set<CompanyStoredFile>() on reference.FileId equals file.Id
                     where reference.CompanyId == company
                     select new { reference.AttachmentId, reference.CheckId, file.Id, file.OwnerCompanyId,
                         file.RetiredAtUtc, Shared = db.Set<CompanyStoredFileReference>().Count(x => x.FileId == file.Id) > 1 })
                .ToListAsync(ct)
            : [];
        foreach (var link in links.Where(x => x.Company == company))
        {
            var currentObject = CompanyDocumentStore.TryGetStorageId(link.Path, out var currentId);
            if (currentObject || link.Path.StartsWith("/CompanyDocument/", StringComparison.Ordinal))
            {
                var matchReference = registered.SingleOrDefault(x => link.Source switch
                {
                    "Çek" => x.CheckId == link.Id,
                    "Fatura" or "Cari" => x.AttachmentId == link.Id,
                    _ => false
                });
                var state = matchReference is null || !link.ParentMatches ||
                    !(currentObject || Guid.TryParseExact(Path.GetFileNameWithoutExtension(link.Path), "N", out currentId)) ||
                    currentId != matchReference.Id || matchReference.RetiredAtUtc.HasValue
                    ? "Belirsiz kayıtlı dosya referansı"
                    : matchReference.Shared || aliases[ComparisonPath(link.Path)] > 1 || matchReference.OwnerCompanyId != company
                        ? "Ortak kayıtlı dosya"
                        : "Sahiplik kaydı eşleşti (dosya özeti silme işleminde doğrulanır)";
                items.Add(new(link.Source, link.Id, state));
                continue;
            }
            var match = LegacyPath.Match(link.Path);
            var expectedFolder = link.Source switch { "Çek" => "cekler", "Fatura" => "faturalar", "Cari" => "cariler", _ => "" };
            var status = aliases[ComparisonPath(link.Path)] > 1 ? "Ortak dosya yolu"
                : !link.ParentMatches || !match.Success || match.Groups["folder"].Value != expectedFolder
                    ? "Belirsiz belge veya dosya bağlantısı"
                    : HasRegularPath(link.Path) ? "Firma ve belge eşleşti, fiziksel sahiplik kanıtı eksik"
                    : "Dosya eksik, erişilemiyor veya bağlantı içeriyor";
            items.Add(new(link.Source, link.Id, status));
        }
        var messages = await db.PrivateMessages.CountAsync(ct);
        var media = await db.PrivateMessages.CountAsync(x => x.MediaKey != null && x.MediaKey != "", ct);
        var presence = await db.PrivatePresences.CountAsync(ct);
        var hidden = await db.PrivateMessageHiddens.CountAsync(ct);
        var storedMedia = await db.PrivateMediaContents.CountAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items, messages, media, presence, hidden, storedMedia);
    }

    internal static string ComparisonPath(string path) =>
        Uri.UnescapeDataString(path.Split('?', '#')[0]).Split('?', '#')[0].Replace('\\', '/').ToLowerInvariant();

    private bool HasRegularPath(string path)
    {
        if (string.IsNullOrEmpty(environment.WebRootPath))
            return false;
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(environment.WebRootPath));
            for (var ancestor = current; ancestor is not null; ancestor = ancestor.Parent)
                if ((File.GetAttributes(ancestor.FullName) & FileAttributes.ReparsePoint) != 0)
                    return false;
            var physical = current.FullName;
            var parts = path.TrimStart('/').Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                physical = Path.Combine(physical, parts[i]);
                var flags = File.GetAttributes(physical);
                if ((flags & FileAttributes.ReparsePoint) != 0 ||
                    ((flags & FileAttributes.Directory) != 0) != (i < parts.Length - 1))
                    return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
