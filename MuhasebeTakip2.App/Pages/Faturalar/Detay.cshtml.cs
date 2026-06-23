using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Microsoft.AspNetCore.Hosting;

namespace MuhasebeTakip2.App.Pages.Faturalar;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DetayModel(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public Fatura? Fatura { get; set; }
    public Firma? Firma { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();
    public List<EkDosya> Dosyalar { get; set; } = new();

    [BindProperty]
    public IFormFile? EkDosya { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Fatura == null)
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostDosyaEkleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var faturaVarMi = await _db.Faturalar.AnyAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (!faturaVarMi)
            return NotFound();

        if (EkDosya == null || EkDosya.Length == 0)
        {
            TempData["Hata"] = "Lütfen bir dosya seçin.";
            return RedirectToPage(new { id });
        }

        var uzanti = Path.GetExtension(EkDosya.FileName).ToLowerInvariant();
        var izinli = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".xlsx", ".xls", ".docx" };
        if (!izinli.Contains(uzanti))
        {
            TempData["Hata"] = "PDF, resim, Excel veya Word dosyası yükleyebilirsiniz.";
            return RedirectToPage(new { id });
        }

        var klasor = Path.Combine(_env.WebRootPath, "uploads", "faturalar");
        Directory.CreateDirectory(klasor);
        var dosyaAdi = $"fatura-{id}-{Guid.NewGuid():N}{uzanti}";
        var fizikselYol = Path.Combine(klasor, dosyaAdi);
        await using var fs = System.IO.File.Create(fizikselYol);
        await EkDosya.CopyToAsync(fs);

        _db.EkDosyalar.Add(new EkDosya
        {
            FirmaId = firmaId.Value,
            FaturaId = id,
            DosyaAdi = Path.GetFileName(EkDosya.FileName),
            DosyaYolu = $"/uploads/faturalar/{dosyaAdi}",
            IcerikTipi = EkDosya.ContentType,
            Boyut = EkDosya.Length,
            YuklemeTarihi = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Dosya faturaya eklendi.";
        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);
        Fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Fatura == null)
            return;

        Hareketler = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && (x.FaturaId == id || x.Aciklama.Contains(Fatura.FaturaNo)))
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        Dosyalar = await _db.EkDosyalar
            .Where(x => x.FirmaId == firmaId && x.FaturaId == id)
            .OrderByDescending(x => x.YuklemeTarihi)
            .ToListAsync();
    }
}