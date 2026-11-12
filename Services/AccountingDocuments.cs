using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public static partial class AccountingDocuments
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    [GeneratedRegex(@"\A[A-Za-z0-9_-][A-Za-z0-9_.-]*\z")]
    private static partial Regex SafeSegment();

    public static void UseAccountingPublicAssets(this WebApplication app)
    {
        // Only public asset roots are eligible for static serving. Uploads are
        // never passed to StaticFileMiddleware, even on anonymous requests.
        app.UseWhen(http =>
        {
            var path = http.Request.Path.Value;
            if (path == null || !path.StartsWith('/')) return false;
            var parts = path[1..].Split('/');
            if (parts.Length == 1 && parts[0] == "favicon.ico")
                return ResolveFile(app.Environment.WebRootPath, parts) != null;
            // The scoped Razor CSS bundle can be supplied by the static web-assets manifest.
            if (parts.Length == 1 && parts[0] == "MuhasebeTakip2.App.styles.css")
            {
                var asset = app.Environment.WebRootFileProvider.GetFileInfo(parts[0]);
                return asset.Exists && asset.PhysicalPath is string physical &&
                    ResolveFile(Path.GetDirectoryName(physical)!, [Path.GetFileName(physical)]) != null;
            }
            return parts.Length > 1 && parts[0] is ("css" or "js" or "lib" or "images") &&
                ResolveFile(app.Environment.WebRootPath, parts) != null;
        }, branch => branch.UseStaticFiles());
    }

    public static void MapAccountingDocuments(this WebApplication app)
    {
        app.MapMethods("/belgeler/ek/{id:int}", ["GET", "HEAD"],
            (int id, HttpContext http, AppDbContext db, IWebHostEnvironment env, IAccountingContextService contexts) =>
                ServeAsync(id, false, http, db, env, contexts)).WithName(SwitchedAccountingBoundary.AttachmentEndpoint);
        app.MapMethods("/belgeler/cek/{id:int}", ["GET", "HEAD"],
            (int id, HttpContext http, AppDbContext db, IWebHostEnvironment env, IAccountingContextService contexts) =>
                ServeAsync(id, true, http, db, env, contexts)).WithName(SwitchedAccountingBoundary.ChequeEndpoint);
    }

    private static async Task<IResult> ServeAsync(int id, bool cheque, HttpContext http, AppDbContext db,
        IWebHostEnvironment env, IAccountingContextService contexts)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'self'";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        var ct = http.RequestAborted;
        int company;
        try
        {
            if (http.Session.GetString("DemoMode") == "1")
            {
                var demoCompany = await AccountingPageContextFilter.VerifiedDemoCompanyAsync(db, http);
                if (demoCompany == null) return Results.NotFound();
                company = demoCompany.Value;
            }
            else
                company = (await contexts.GetCurrentAsync(http, ct)).ActiveCompanyId;
        }
        catch (AccountingContextDeniedException)
        {
            return Results.NotFound();
        }

        string? path;
        string? originalName = null;
        if (cheque)
        {
            path = await FindChequeFileAsync(db, env, company, id, ct);
        }
        else
        {
            var document = await db.EkDosyalar.AsNoTracking()
                .Where(x => x.Id == id && x.FirmaId == company && x.Firma != null && x.Firma.AktifMi)
                .Where(x => (x.FaturaId != null && x.CariKartId == null && x.Fatura != null &&
                             x.Fatura.FirmaId == company && (x.Fatura.CariKartId == null ||
                             (x.Fatura.CariKart != null && x.Fatura.CariKart.FirmaId == company))) ||
                            (x.CariKartId != null && x.FaturaId == null && x.CariKart != null &&
                             x.CariKart.FirmaId == company))
                .Select(x => new { x.DosyaYolu, x.DosyaAdi, x.FaturaId, x.CariKartId }).SingleOrDefaultAsync(ct);
            if (document == null) return Results.NotFound();
            var kind = document.FaturaId.HasValue ? "faturalar" : "cariler";
            var prefix = document.FaturaId.HasValue ? $"fatura-{document.FaturaId}-" : $"cari-{document.CariKartId}-";
            path = ResolveDocument(env.WebRootPath, document.DosyaYolu, kind, prefix);
            if (path != null && !await IsUniquePathAsync(db, document.DosyaYolu, id, null, ct)) path = null;
            originalName = document.DosyaAdi;
        }

        if (path == null) return Results.NotFound();

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.NotFound();
        }

        var extension = Path.GetExtension(path);
        var name = $"belge-{id}{extension}";
        if (!string.IsNullOrWhiteSpace(originalName) && originalName.Length <= 180 &&
            !originalName.Any(c => char.IsControl(c) || c is '/' or '\\') &&
            string.Equals(Path.GetExtension(originalName), extension, StringComparison.OrdinalIgnoreCase))
            name = originalName;

        var contentType = ContentTypes[extension];
        var disposition = new ContentDispositionHeaderValue(
            http.Request.Query.ContainsKey("indir") || (!contentType.StartsWith("image/") && contentType != "application/pdf")
                ? "attachment" : "inline");
        disposition.SetHttpFileName(name);
        http.Response.Headers.ContentDisposition = disposition.ToString();
        return Results.File(stream, contentType, enableRangeProcessing: true);
    }

    internal static async Task<int?> VerifiedCompanyAsync(AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var user = http.Session.GetInt32("KullaniciId");
        var company = http.Session.GetInt32("FirmaId");
        if (http.Session.GetString("PrivateMode") == "1" || user is null or <= 0 || company is null or <= 0)
            return null;
        return await db.Kullanicilar.AsNoTracking().AnyAsync(x => x.Id == user && x.FirmaId == company &&
            x.Firma != null && x.Firma.AktifMi, ct) ? company : null;
    }
    internal static async Task<string?> FindChequeFileAsync(AppDbContext db, IWebHostEnvironment env,
        int company, int id, CancellationToken ct)
    {
        var stored = await db.Cekler.AsNoTracking()
            .Where(x => x.Id == id && x.FirmaId == company && x.Firma != null && x.Firma.AktifMi)
            .Select(x => x.ResimYolu).SingleOrDefaultAsync(ct);
        var path = ResolveDocument(env.WebRootPath, stored, "cekler", "");
        return path != null && await IsUniquePathAsync(db, stored!, null, id, ct) ? path : null;
    }

    private static async Task<bool> IsUniquePathAsync(AppDbContext db, string stored, int? attachmentId, int? chequeId, CancellationToken ct)
    {
        // Names produced by the app are ASCII. Compare case-insensitively so
        // paths that alias on Windows cannot attach a file to another record.
        var key = stored.ToLowerInvariant();
        return !await db.EkDosyalar.AnyAsync(x => x.Id != attachmentId && x.DosyaYolu.ToLower() == key, ct) &&
            !await db.Cekler.AnyAsync(x => x.Id != chequeId && x.ResimYolu != null && x.ResimYolu.ToLower() == key, ct);
    }

    private static string? ResolveDocument(string root, string? stored, string kind, string prefix)
    {
        if (stored == null || !stored.StartsWith("/uploads/", StringComparison.Ordinal)) return null;
        var parts = stored[1..].Split('/');
        var filename = parts[^1];
        var extension = Path.GetExtension(filename);
        if (!ContentTypes.TryGetValue(extension, out var mime) ||
            (kind == "cekler" && !mime.StartsWith("image/") && mime != "application/pdf")) return null;

        var stem = Path.GetFileNameWithoutExtension(filename);
        if (parts.Length == 4 && parts[1] == "yedek")
        {
            if (!Guid.TryParseExact(parts[2], "N", out _) || !Guid.TryParseExact(stem, "N", out _)) return null;
        }
        else if (parts.Length != 3 || parts[1] != kind || !stem.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(stem[prefix.Length..], kind == "cekler" ? "D" : "N", out _))
        {
            return null;
        }

        return ResolveFile(root, parts);
    }

    private static string? ResolveFile(string root, string[] parts)
    {
        try
        {
            var path = Path.GetFullPath(root);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            foreach (var part in parts)
            {
                if (!SafeSegment().IsMatch(part) || part.EndsWith('.')) return null;
                path = Path.Combine(path, part);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            }
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
