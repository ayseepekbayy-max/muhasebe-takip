using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Odemeler;

public class DuzenleModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public DuzenleModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    [BindProperty]
    public OdemeDuzenleForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var odeme = await _db.OdemePlanlari.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (odeme == null)
            return NotFound();

        Form = new OdemeDuzenleForm
        {
            Id = odeme.Id,
            OdemeAdi = odeme.OdemeAdi,
            OdemeTuru = odeme.OdemeTuru,
            Aciklama = odeme.Aciklama,
            AylikOdemeTutari = odeme.AylikOdemeTutari,
            ToplamTaksitSayisi = odeme.ToplamTaksitSayisi,
            KalanTaksitSayisi = odeme.KalanTaksitSayisi,
            IlkOdemeTarihi = odeme.IlkOdemeTarihi.Date,
            SonrakiOdemeTarihi = odeme.SonrakiOdemeTarihi.Date,
            OdemeGunu = odeme.OdemeGunu,
            BildirimGunu = odeme.BildirimGunu,
            BildirimAktifMi = odeme.BildirimAktifMi,
            OtomatikTaksitDusur = odeme.OtomatikTaksitDusur,
            AktifMi = odeme.AktifMi
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var odeme = await _db.OdemePlanlari.FirstOrDefaultAsync(x => x.Id == Form.Id && x.FirmaId == firmaId.Value);
        if (odeme == null)
            return NotFound();

        Form.Temizle();
        Form.OdemeGunu = Form.IlkOdemeTarihi.Day;
        Dogrula();
        if (!ModelState.IsValid)
            return Page();

        var eskiDeger = new
        {
            odeme.Id,
            odeme.OdemeAdi,
            odeme.OdemeTuru,
            odeme.AylikOdemeTutari,
            odeme.KalanTaksitSayisi,
            odeme.SonrakiOdemeTarihi,
            odeme.AktifMi
        };

        odeme.OdemeAdi = Form.OdemeAdi;
        odeme.OdemeTuru = Form.OdemeTuru;
        odeme.Aciklama = Form.Aciklama;
        odeme.AylikOdemeTutari = Form.AylikOdemeTutari;
        odeme.ToplamTaksitSayisi = Form.ToplamTaksitSayisi;
        odeme.KalanTaksitSayisi = Math.Max(0, Form.KalanTaksitSayisi);
        odeme.OdemeGunu = Form.OdemeGunu;
        odeme.IlkOdemeTarihi = OdemePlanlamaService.AyIcinGecerliGun(Form.IlkOdemeTarihi, Form.OdemeGunu);
        odeme.SonrakiOdemeTarihi = OdemePlanlamaService.AyIcinGecerliGun(Form.SonrakiOdemeTarihi, Form.OdemeGunu);
        odeme.SonOdemeTarihi = OdemePlanlamaService.TahminiSonOdemeTarihi(odeme.IlkOdemeTarihi, Form.OdemeGunu, Form.ToplamTaksitSayisi);
        odeme.BildirimGunu = Form.BildirimGunu;
        odeme.BildirimAktifMi = Form.BildirimAktifMi;
        odeme.OtomatikTaksitDusur = Form.OtomatikTaksitDusur;
        odeme.AktifMi = Form.AktifMi;
        odeme.SonOdemeYapildiMi = false;
        odeme.GuncellemeTarihi = DateTime.UtcNow;

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Ödemeler",
                "Düzenleme",
                $"Ödeme planı düzenlendi: {odeme.OdemeAdi} (ID: {odeme.Id}).",
                eskiDeger,
                new
                {
                    odeme.Id,
                    odeme.OdemeAdi,
                    odeme.OdemeTuru,
                    odeme.AylikOdemeTutari,
                    odeme.KalanTaksitSayisi,
                    odeme.SonrakiOdemeTarihi,
                    odeme.AktifMi
                }),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Ödeme planı güncellendi.";
        return RedirectToPage("/Odemeler/Detay", new { id = odeme.Id });
    }

    private void Dogrula()
    {
        if (string.IsNullOrWhiteSpace(Form.OdemeAdi))
            ModelState.AddModelError("", "Ödeme adı zorunludur.");
        if (Form.AylikOdemeTutari <= 0)
            ModelState.AddModelError("", "Aylık ödeme tutarı sıfırdan büyük olmalıdır.");
        if (Form.ToplamTaksitSayisi <= 0)
            ModelState.AddModelError("", "Toplam taksit sayısı sıfırdan büyük olmalıdır.");
        if (Form.KalanTaksitSayisi < 0)
            ModelState.AddModelError("", "Kalan taksit sayısı sıfırdan küçük olamaz.");
        if (Form.KalanTaksitSayisi > Form.ToplamTaksitSayisi)
            ModelState.AddModelError("", "Kalan taksit sayısı toplam taksit sayısından büyük olamaz.");
    }

    public class OdemeDuzenleForm
    {
        public int Id { get; set; }
        public string OdemeAdi { get; set; } = "";
        public OdemeTuru OdemeTuru { get; set; } = OdemeTuru.Diger;
        public string? Aciklama { get; set; }
        public decimal AylikOdemeTutari { get; set; }
        public int ToplamTaksitSayisi { get; set; } = 1;
        public int KalanTaksitSayisi { get; set; } = 1;
        public DateTime IlkOdemeTarihi { get; set; } = DateTime.UtcNow.Date;
        public DateTime SonrakiOdemeTarihi { get; set; } = DateTime.UtcNow.Date;
        public int OdemeGunu { get; set; } = DateTime.UtcNow.Day;
        public int BildirimGunu { get; set; } = 3;
        public bool BildirimAktifMi { get; set; } = true;
        public bool OtomatikTaksitDusur { get; set; } = true;
        public bool AktifMi { get; set; } = true;

        public void Temizle()
        {
            OdemeAdi = (OdemeAdi ?? "").Trim();
            Aciklama = string.IsNullOrWhiteSpace(Aciklama) ? null : Aciklama.Trim();
            OdemeGunu = IlkOdemeTarihi.Day;
            BildirimGunu = Math.Max(0, BildirimGunu);
        }
    }
}