using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.CariKartlar;

public class EkstreModel : PageModel
{
    private readonly AppDbContext _db;

    public EkstreModel(AppDbContext db)
    {
        _db = db;
    }

    public CariKart? Cari { get; set; }
    public List<EkstreSatiri> Satirlar { get; set; } = new();
    public decimal ToplamBorc { get; set; }
    public decimal ToplamAlacak { get; set; }
    public decimal Bakiye => ToplamBorc - ToplamAlacak;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Cari = await _db.CariKartlar.FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (Cari == null)
            return NotFound();

        var faturalar = await _db.Faturalar
            .Where(x => x.FirmaId == firmaId.Value && x.CariKartId == id)
            .OrderBy(x => x.Tarih)
            .ToListAsync();

        var hareketler = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.CariKartId == id)
            .OrderBy(x => x.Tarih)
            .ToListAsync();

        foreach (var fatura in faturalar)
        {
            var borc = fatura.Tip == FaturaTipi.Satis ? fatura.GenelToplam : 0;
            var alacak = fatura.Tip == FaturaTipi.Alis ? fatura.GenelToplam : 0;
            Satirlar.Add(new EkstreSatiri
            {
                Tarih = fatura.Tarih,
                Tur = fatura.Tip == FaturaTipi.Satis ? "Satış Faturası" : "Alış Faturası",
                Aciklama = fatura.FaturaNo,
                Borc = borc,
                Alacak = alacak,
                FaturaId = fatura.Id
            });
        }

        foreach (var hareket in hareketler)
        {
            var borc = hareket.Tip == HareketTipi.Cikis ? hareket.Tutar : 0;
            var alacak = hareket.Tip == HareketTipi.Giris ? hareket.Tutar : 0;
            Satirlar.Add(new EkstreSatiri
            {
                Tarih = hareket.Tarih,
                Tur = hareket.Tip == HareketTipi.Giris ? "Tahsilat" : "Ödeme",
                Aciklama = hareket.Aciklama,
                Borc = borc,
                Alacak = alacak,
                FaturaId = hareket.FaturaId
            });
        }

        Satirlar = Satirlar
            .OrderBy(x => x.Tarih)
            .ThenBy(x => x.Tur)
            .ToList();

        decimal bakiye = 0;
        foreach (var satir in Satirlar)
        {
            bakiye += satir.Borc - satir.Alacak;
            satir.Bakiye = bakiye;
        }

        ToplamBorc = Satirlar.Sum(x => x.Borc);
        ToplamAlacak = Satirlar.Sum(x => x.Alacak);

        return Page();
    }

    public class EkstreSatiri
    {
        public DateTime Tarih { get; set; }
        public string Tur { get; set; } = "";
        public string Aciklama { get; set; } = "";
        public decimal Borc { get; set; }
        public decimal Alacak { get; set; }
        public decimal Bakiye { get; set; }
        public int? FaturaId { get; set; }
    }
}