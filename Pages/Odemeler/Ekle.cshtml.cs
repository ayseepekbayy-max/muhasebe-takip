using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Odemeler;

public class EkleModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public EkleModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    [BindProperty]
    public OdemeForm Form { get; set; } = new();

    public IActionResult OnGet()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Form.IlkOdemeTarihi = DateTime.UtcNow.Date;
        Form.OdemeGunu = DateTime.UtcNow.Day;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Form.Temizle();
        Form.OdemeGunu = Form.IlkOdemeTarihi.Day;
        Dogrula();

        if (!ModelState.IsValid)
            return Page();

        var ilkOdemeTarihi = OdemePlanlamaService.AyIcinGecerliGun(Form.IlkOdemeTarihi, Form.OdemeGunu);
        var kalanTaksit = Math.Max(0, Form.KalanTaksitSayisi ?? Form.ToplamTaksitSayisi);
        var baslangictaTamamlandi = kalanTaksit == 0;

        var odeme = new OdemePlani
        {
            FirmaId = firmaId.Value,
            OdemeAdi = Form.OdemeAdi,
            OdemeTuru = Form.OdemeTuru,
            Aciklama = Form.Aciklama,
            AylikOdemeTutari = Form.AylikOdemeTutari,
            ToplamTaksitSayisi = Form.ToplamTaksitSayisi,
            KalanTaksitSayisi = kalanTaksit,
            OdemeGunu = Form.OdemeGunu,
            IlkOdemeTarihi = ilkOdemeTarihi,
            SonrakiOdemeTarihi = baslangictaTamamlandi ? null : ilkOdemeTarihi,
            SonOdemeTarihi = OdemePlanlamaService.TahminiSonOdemeTarihi(ilkOdemeTarihi, Form.OdemeGunu, Form.ToplamTaksitSayisi),
            BildirimGunu = Form.BildirimGunu,
            BildirimAktifMi = Form.BildirimAktifMi,
            OtomatikTaksitDusur = Form.OtomatikTaksitDusur,
            AktifMi = baslangictaTamamlandi ? false : Form.AktifMi,
            TamamlandiMi = baslangictaTamamlandi,
            TamamlanmaTarihi = null,
            OlusturanKullaniciId = HttpContext.Session.GetInt32("KullaniciId"),
            OlusturanKullaniciAdi = HttpContext.Session.GetString("KullaniciAdi"),
            OlusturmaTarihi = DateTime.UtcNow
        };

        _db.OdemePlanlari.Add(odeme);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Ödemeler",
                "Ekleme",
                $"Ödeme planı eklendi: {odeme.OdemeAdi} (ID: {odeme.Id}).",
                yeniDeger: new
                {
                    odeme.Id,
                    odeme.OdemeAdi,
                    odeme.OdemeTuru,
                    odeme.AylikOdemeTutari,
                    odeme.KalanTaksitSayisi,
                    odeme.SonrakiOdemeTarihi,
                    odeme.TamamlandiMi
                }),
            anaKaydiOnceKaydet: true);

        TempData["Basari"] = "Ödeme planı eklendi.";
        return RedirectToPage("/Odemeler/Index");
    }

    private void Dogrula()
    {
        if (string.IsNullOrWhiteSpace(Form.OdemeAdi))
            ModelState.AddModelError("", "Ödeme adı zorunludur.");
        if (Form.AylikOdemeTutari <= 0)
            ModelState.AddModelError("", "Aylık ödeme tutarı sıfırdan büyük olmalıdır.");
        if (Form.ToplamTaksitSayisi <= 0)
            ModelState.AddModelError("", "Toplam taksit sayısı sıfırdan büyük olmalıdır.");
        if (Form.KalanTaksitSayisi.HasValue && Form.KalanTaksitSayisi.Value < 0)
            ModelState.AddModelError("", "Kalan taksit sayısı sıfırdan küçük olamaz.");
        if (Form.KalanTaksitSayisi.HasValue && Form.KalanTaksitSayisi.Value > Form.ToplamTaksitSayisi)
            ModelState.AddModelError("", "Kalan taksit sayısı toplam taksit sayısından büyük olamaz.");
        if (Form.BildirimGunu < 0)
            ModelState.AddModelError("", "Bildirim günü negatif olamaz.");
    }

    public class OdemeForm
    {
        public string OdemeAdi { get; set; } = "";
        public OdemeTuru OdemeTuru { get; set; } = OdemeTuru.Diger;
        public string? Aciklama { get; set; }
        public decimal AylikOdemeTutari { get; set; }
        public int ToplamTaksitSayisi { get; set; } = 1;
        public int? KalanTaksitSayisi { get; set; }
        public DateTime IlkOdemeTarihi { get; set; } = DateTime.UtcNow.Date;
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
