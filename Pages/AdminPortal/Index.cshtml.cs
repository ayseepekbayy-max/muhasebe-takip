using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.AdminPortal;

[ServiceFilter(typeof(AdminPortalAuthorizationFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(AppDbContext db, AccountingCompanySwitchPolicy? policy = null) : PageModel
{
    public List<CompanySummary> Companies { get; private set; } = new();
    public int UserCount => Companies.Sum(x => x.UserCount);
    public AccountingSwitchControl? SwitchControl { get; private set; }
    public string ActiveCompanyName { get; private set; } = "";
    public bool CanOpenOffice { get; private set; }
    public string OfficeName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = HttpContext.Session.GetInt32("KullaniciId");
        var office = await db.Kullanicilar.AsNoTracking()
            .Where(x => x.Id == userId && x.Firma != null && x.Firma.AktifMi)
            .Select(x => new { x.FirmaId, x.Firma!.FirmaAdi })
            .SingleOrDefaultAsync(cancellationToken);
        // The accounting home uses Session FirmaId. Never link to a different office or mutate session.
        CanOpenOffice = office != null && HttpContext.Session.GetInt32("FirmaId") == office.FirmaId;
        OfficeName = office?.FirmaAdi ?? "";

        if (policy?.Enabled == true)
        {
            try
            {
                SwitchControl = await HttpContext.RequestServices.GetRequiredService<IAccountingContextService>()
                    .GetSwitchControlAsync(HttpContext, cancellationToken);
            }
            catch (AccountingContextDeniedException) { return StatusCode(403); }

            var current = SwitchControl.Context;
            ActiveCompanyName = await db.Firmalar.AsNoTracking().Where(x => x.Id == current.ActiveCompanyId)
                .Select(x => x.FirmaAdi).SingleOrDefaultAsync(cancellationToken) ?? "Kullanılamayan firma";
            CanOpenOffice = current.ActiveCompanyId == current.Actor.HomeCompanyId;
            ViewData["AccountingContextStamp"] = current.Stamp;
            if (!SwitchControl.ActiveCompanyAvailable)
                return Page();
        }

        Companies = await db.Firmalar.AsNoTracking()
            .OrderBy(x => x.FirmaAdi).ThenBy(x => x.Id)
            .Select(x => new CompanySummary(x.Id, x.FirmaAdi, x.AktifMi,
                db.Kullanicilar.Count(user => user.FirmaId == x.Id)))
            .ToListAsync(cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnPostSecAsync(int targetCompanyId, CancellationToken cancellationToken) =>
        SwitchAsync(targetCompanyId, cancellationToken);

    public Task<IActionResult> OnPostGeriDonAsync(CancellationToken cancellationToken) =>
        SwitchAsync(null, cancellationToken);

    private async Task<IActionResult> SwitchAsync(int? target, CancellationToken ct)
    {
        if (policy?.Enabled != true)
            return StatusCode(403);
        if (!Request.HasFormContentType) return StatusCode(409);
        var form = await Request.ReadFormAsync(ct);
        if (form["AccountingCompanyId"].Count != 1 || form["AccountingVersion"].Count != 1 ||
            !int.TryParse(form["AccountingCompanyId"][0], NumberStyles.None, CultureInfo.InvariantCulture, out var company) ||
            !Guid.TryParseExact(form["AccountingVersion"][0], "D", out var version))
            return StatusCode(409);
        if (target.HasValue && (!ModelState.IsValid || form["targetCompanyId"].Count != 1 ||
            !int.TryParse(form["targetCompanyId"][0], NumberStyles.None, CultureInfo.InvariantCulture, out var postedTarget) ||
            postedTarget != target.Value))
            return StatusCode(403);

        if (form["AccountingSwitchToken"].Count != 1) return StatusCode(409);

        var contexts = HttpContext.RequestServices.GetRequiredService<IAccountingContextService>();
        var result = await contexts.SwitchAsync(HttpContext, target, new(company, version), form["AccountingSwitchToken"][0] ?? "", ct);
        return result switch
        {
            AccountingSwitchResult.Applied => RedirectToPage(target.HasValue ? "/Kasa/Hareketler" : "/Index"),
            AccountingSwitchResult.Stale => StatusCode(409),
            AccountingSwitchResult.Denied => StatusCode(403),
            _ => new ContentResult
            {
                StatusCode = 503,
                ContentType = "text/plain; charset=utf-8",
                Content = result == AccountingSwitchResult.Uncertain
                    ? "Geçiş sonucu doğrulanamadı. Yeniden göndermeden önce portalı açıp aktif firmayı kontrol edin."
                    : "Firma değiştirilemedi. Muhasebe bağlamınız korunuyor."
            }
        };
    }

    public sealed record CompanySummary(int Id, string Name, bool IsActive, int UserCount);
}
