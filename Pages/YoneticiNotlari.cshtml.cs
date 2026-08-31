using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class YoneticiNotlariModel : PageModel
{
    private const int NotMetniMaksimumUzunluk = 500;
    private static readonly TimeSpan NotGecerlilikSuresi = TimeSpan.FromHours(2);
    private readonly AppDbContext _db;

    public YoneticiNotlariModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        return await AdminBaglaminiGetirAsync() == null
            ? StatusCode(StatusCodes.Status403Forbidden)
            : new NoContentResult();
    }

    public async Task<IActionResult> OnGetListeAsync()
    {
        var baglam = await AdminBaglaminiGetirAsync();
        if (baglam == null)
            return StatusCode(StatusCodes.Status403Forbidden);

        var simdi = DateTime.UtcNow;
        var suresiDolanlar = await _db.YoneticiNotlari
            .Where(x => x.FirmaId == baglam.Value.FirmaId && x.SonKullanmaTarihi <= simdi)
            .ToListAsync();

        if (suresiDolanlar.Count > 0)
        {
            _db.YoneticiNotlari.RemoveRange(suresiDolanlar);
            await _db.SaveChangesAsync();
        }

        var notlar = await _db.YoneticiNotlari
            .AsNoTracking()
            .Where(x => x.FirmaId == baglam.Value.FirmaId && x.SonKullanmaTarihi > simdi)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .Select(x => new
            {
                x.Id,
                x.NotMetni,
                x.OlusturmaTarihi,
                x.SonKullanmaTarihi
            })
            .ToListAsync();

        return new JsonResult(notlar);
    }

    public async Task<IActionResult> OnPostEkleAsync(string? notMetni)
    {
        var baglam = await AdminBaglaminiGetirAsync();
        if (baglam == null)
            return StatusCode(StatusCodes.Status403Forbidden);

        notMetni = (notMetni ?? "").Trim();
        if (notMetni.Length == 0)
            return BadRequest(new { mesaj = "Not metni boş olamaz." });

        if (notMetni.Length > NotMetniMaksimumUzunluk)
            return BadRequest(new { mesaj = $"Not en fazla {NotMetniMaksimumUzunluk} karakter olabilir." });

        var simdi = DateTime.UtcNow;
        var not = new YoneticiNotu
        {
            FirmaId = baglam.Value.FirmaId,
            KullaniciId = baglam.Value.KullaniciId,
            NotMetni = notMetni,
            OlusturmaTarihi = simdi,
            SonKullanmaTarihi = simdi.Add(NotGecerlilikSuresi)
        };

        _db.YoneticiNotlari.Add(not);
        await _db.SaveChangesAsync();

        return new JsonResult(new { mesaj = "Not eklendi." });
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var baglam = await AdminBaglaminiGetirAsync();
        if (baglam == null)
            return StatusCode(StatusCodes.Status403Forbidden);

        var not = await _db.YoneticiNotlari
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == baglam.Value.FirmaId);

        if (not == null)
            return NotFound(new { mesaj = "Not bulunamadı." });

        _db.YoneticiNotlari.Remove(not);
        await _db.SaveChangesAsync();

        return new JsonResult(new { mesaj = "Not silindi." });
    }

    private async Task<(int FirmaId, int KullaniciId)?> AdminBaglaminiGetirAsync()
    {
        var rol = (HttpContext.Session.GetString("Rol") ?? "").Trim().ToLowerInvariant();
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        var kullaniciId = HttpContext.Session.GetInt32("KullaniciId");

        if ((rol != "superadmin" && rol != "admin") || firmaId is null or <= 0 || kullaniciId is null or <= 0)
            return null;

        var kullaniciGecerliMi = await _db.Kullanicilar.AnyAsync(x =>
            x.Id == kullaniciId.Value &&
            x.FirmaId == firmaId.Value &&
            x.Firma != null &&
            x.Firma.AktifMi &&
            (x.Rol.ToLower() == "superadmin" || x.Rol.ToLower() == "admin"));

        return kullaniciGecerliMi ? (firmaId.Value, kullaniciId.Value) : null;
    }
}
