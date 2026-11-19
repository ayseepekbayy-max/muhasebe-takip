using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.SuperAdmin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ServiceFilter(typeof(AdminPortalAuthorizationFilter))]
[ValidateAntiForgeryToken]
public sealed class FirmaSilmeOnizlemeModel(
    AppDbContext db, IAdminPortalAuthorizationService portalAuthorization, IWebHostEnvironment environment) : PageModel
{
    public string FirmaAdi { get; private set; } = "";
    public int FirmaId { get; private set; }
    public CompanyDeletionInventory.Result Inventory { get; private set; } = default!;
    public LegacyCompanyFileReport.Report LegacyFiles { get; private set; } = default!;
    public string? Confirmation { get; private set; }

    internal static bool HedefUygun(int firmaId, int yoneticiFirmaId) =>
        firmaId > 1 && firmaId != yoneticiFirmaId;

    internal static async Task<int?> YoneticiFirmaIdAsync(
        AppDbContext db, IAdminPortalAuthorizationService authorization, HttpContext context, CancellationToken ct)
    {
        if (!await authorization.IsAuthorizedAsync(context, ct))
            return null;

        var kullaniciId = context.Session.GetInt32("KullaniciId");
        var oturumFirmaId = context.Session.GetInt32("FirmaId");
        if (oturumFirmaId is null or <= 0)
            return null;

        return await db.Kullanicilar.AsNoTracking()
            .Where(x => x.Id == kullaniciId && x.FirmaId == oturumFirmaId &&
                x.Rol == "SuperAdmin" && x.Firma != null && x.Firma.AktifMi)
            .Select(x => (int?)x.FirmaId).SingleOrDefaultAsync(ct);
    }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var yoneticiFirmaId = await YoneticiFirmaIdAsync(db, portalAuthorization, HttpContext, ct);
        if (yoneticiFirmaId is null || !HedefUygun(id, yoneticiFirmaId.Value))
            return StatusCode(403);

        var firma = await db.Firmalar.AsNoTracking()
            .Where(x => x.Id == id).Select(x => new { x.FirmaAdi }).SingleOrDefaultAsync(ct);
        if (firma is null)
            return NotFound();

        FirmaAdi = firma.FirmaAdi;
        FirmaId = id;
        Inventory = await new CompanyDeletionInventory(db).InspectAsync(id, ct);
        LegacyFiles = await new LegacyCompanyFileReport(db, environment).InspectAsync(id, ct);
        if (DeletionEnabled() && db.Database.IsNpgsql() && !HasBlockers() && !HasUnattributedMedia() &&
            (LegacyFiles.Items.Count == 0 || CleanupEnabled()) &&
            Guid.TryParseExact(HttpContext.Session.GetString(AccountingSessionLifecycle.EpochKey), "N", out _))
            Confirmation = Protector().Protect(ConfirmationValue(id, Guid.NewGuid()), TimeSpan.FromMinutes(5));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, string? confirmation, CancellationToken ct)
    {
        var adminCompany = await YoneticiFirmaIdAsync(db, portalAuthorization, HttpContext, ct);
        if (adminCompany is null || !HedefUygun(id, adminCompany.Value))
            return StatusCode(403);
        if (!DeletionEnabled() || !db.Database.IsNpgsql())
            return Blocked("Kalıcı firma silme kapalı.");
        if (!CleanupEnabled() && (await db.EkDosyalar.AnyAsync(x => x.FirmaId == id, ct) ||
            await db.Cekler.AnyAsync(x => x.FirmaId == id && x.ResimYolu != null && x.ResimYolu != "", ct)))
            return Blocked("Belge temizleme işçisi etkin değil; dosyalı firma silme durduruldu.");
        if (HasUnattributedMedia())
            return Blocked("Özel medya dosyalarının firma aidiyeti doğrulanamadı.");
        if (confirmation is null)
            return BadRequest();
        Guid operation;
        try
        {
            var value = Protector().Unprotect(confirmation);
            var fields = value.Split('/');
            if (fields.Length != 6 || !Guid.TryParseExact(fields[^1], "N", out operation) ||
                value != ConfirmationValue(id, operation))
                return BadRequest();
        }
        catch (System.Security.Cryptography.CryptographicException) { return BadRequest(); }

        try
        {
            var store = HttpContext.RequestServices.GetRequiredService<CompanyDocumentStore>();
            await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(id, adminCompany.Value,
                HttpContext.Session.GetInt32("KullaniciId")!.Value, operation, store, ct);
            return RedirectToPage("/SuperAdmin/Index");
        }
        catch (InvalidDataException) { return Blocked("Firma kapsamı doğrulanamadı. Özel panel kayıtlarını, eski veya ortak dosyaları ve veritabanı korumalarını kontrol edin."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Blocked("Silme işleminin güvenli süre sınırı doldu. Otomatik tekrar yapılmadı; yeniden onay vermeden önce firma durumunu kontrol edin.");
        }
        catch (IOException ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<FirmaSilmeOnizlemeModel>>()
                .LogWarning("Company deletion file operation failed ({ErrorType}, {ErrorCode}).", ex.GetType().Name, ex.HResult);
            return Blocked("Dosya veya onay kaydı doğrulanamadı. İşlemi yeniden göndermeden önce firma durumunu kontrol edin.");
        }
        catch (Npgsql.NpgsqlException ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<FirmaSilmeOnizlemeModel>>()
                .LogWarning("Company deletion database operation failed ({SqlState}).", ex.SqlState);
            return Blocked("Veritabanı işlemi doğrulanamadı. İşlemi yeniden göndermeden önce firma durumunu kontrol edin.");
        }
    }

    private bool DeletionEnabled() =>
        HttpContext.RequestServices?.GetService<IConfiguration>()?.GetValue<bool>("CompanyDeletion:Enabled") == true;
    private bool CleanupEnabled() =>
        HttpContext.RequestServices?.GetService<IConfiguration>()?.GetValue<bool>("CompanyDocuments:CleanupEnabled") == true;

    private bool HasBlockers() => LegacyFiles.HasUnattributedPrivateData ||
        Inventory.Tables.Any(x => x.HasExternalReferences) ||
        LegacyFiles.Items.Any(x => !x.Status.StartsWith("Sahiplik kaydı eşleşti", StringComparison.Ordinal));

    private bool HasUnattributedMedia()
    {
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "PrivateMedia");
        try { return Directory.EnumerateFileSystemEntries(root).Any(); }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private ITimeLimitedDataProtector Protector() => HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("Firmova.CompanyDeletion.Confirmation.v3").ToTimeLimitedDataProtector();

    private string ConfirmationValue(int id, Guid operation) =>
        $"{HttpContext.Session.Id}/{HttpContext.Session.GetString(AccountingSessionLifecycle.EpochKey)}/{HttpContext.Session.GetInt32("KullaniciId")}/{HttpContext.Session.GetInt32("FirmaId")}/{id}/{operation:N}";

    private static ContentResult Blocked(string message) => new()
        { StatusCode = 409, ContentType = "text/plain; charset=utf-8", Content = message };
}
