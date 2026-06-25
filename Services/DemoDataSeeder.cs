using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class DemoDataSeeder
{
    public const string DemoUserName = "demo";
    public const string DemoEmail = "demo@firmova.local";
    private const string DemoPassword = "demo123";
    private const string DemoSeedInvoiceNo = "NVM-2026-0001";
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    public static async Task<Kullanici> PrepareDemoAsync(AppDbContext db)
    {
        await SeedLock.WaitAsync();
        try
        {
            var kullanici = await db.Kullanicilar
                .Include(x => x.Firma)
                .FirstOrDefaultAsync(x => x.KullaniciAdi == DemoUserName || x.Email == DemoEmail);

            if (kullanici?.Firma == null)
            {
                var firma = new Firma
                {
                    FirmaAdi = "Nova Mobilya ve Tasarım Ltd. Şti.",
                    AktifMi = true,
                    Adres = "Merkez Mah. Tasarım Cad. No: 18",
                    Telefon = "0212 345 67 89",
                    Email = "demo@firmova.com",
                    VergiDairesi = "Merkez Vergi Dairesi",
                    VergiNo = "1234567890"
                };

                db.Firmalar.Add(firma);
                await db.SaveChangesAsync();

                kullanici = new Kullanici
                {
                    KullaniciAdi = DemoUserName,
                    Email = DemoEmail,
                    Sifre = PasswordHelper.Hash(DemoPassword),
                    FirmaId = firma.Id,
                    Firma = firma,
                    Rol = "Demo"
                };

                db.Kullanicilar.Add(kullanici);
                await db.SaveChangesAsync();
            }
            else
            {
                kullanici.Sifre = PasswordHelper.Hash(DemoPassword);
                kullanici.Rol = "Demo";
                kullanici.Firma.AktifMi = true;
                kullanici.Firma.FirmaAdi = "Nova Mobilya ve Tasarım Ltd. Şti.";
                kullanici.Firma.Adres = "Merkez Mah. Tasarım Cad. No: 18";
                kullanici.Firma.Telefon = "0212 345 67 89";
                kullanici.Firma.Email = "demo@firmova.com";
                kullanici.Firma.VergiDairesi = "Merkez Vergi Dairesi";
                kullanici.Firma.VergiNo = "1234567890";
                await db.SaveChangesAsync();
            }

            if (await DemoDataNeedsSeedAsync(db, kullanici.FirmaId))
                await ResetDemoDataAsync(db, kullanici.FirmaId);

            return await db.Kullanicilar
                .Include(x => x.Firma)
                .FirstAsync(x => x.Id == kullanici.Id);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task<bool> DemoDataNeedsSeedAsync(AppDbContext db, int firmaId)
    {
        var hasSeedInvoice = await db.Faturalar
            .AnyAsync(x => x.FirmaId == firmaId && x.FaturaNo == DemoSeedInvoiceNo);
        var hasCari = await db.CariKartlar.AnyAsync(x => x.FirmaId == firmaId);
        var hasStock = await db.StokUrunler.AnyAsync(x => x.FirmaId == firmaId);
        var hasCashMovement = await db.KasaHareketleri.AnyAsync(x => x.FirmaId == firmaId);

        return !hasSeedInvoice || !hasCari || !hasStock || !hasCashMovement;
    }

    private static async Task ResetDemoDataAsync(AppDbContext db, int firmaId)
    {
        var faturaIds = await db.Faturalar
            .Where(x => x.FirmaId == firmaId)
            .Select(x => x.Id)
            .ToListAsync();

        db.EkDosyalar.RemoveRange(db.EkDosyalar.Where(x => x.FirmaId == firmaId));
        db.KasaHareketleri.RemoveRange(db.KasaHareketleri.Where(x => x.FirmaId == firmaId));
        db.FaturaKalemleri.RemoveRange(db.FaturaKalemleri.Where(x => faturaIds.Contains(x.FaturaId)));
        db.Faturalar.RemoveRange(db.Faturalar.Where(x => x.FirmaId == firmaId));
        db.CariKartlar.RemoveRange(db.CariKartlar.Where(x => x.FirmaId == firmaId));
        db.StokHareketleri.RemoveRange(db.StokHareketleri.Where(x => x.FirmaId == firmaId));
        db.StokUrunler.RemoveRange(db.StokUrunler.Where(x => x.FirmaId == firmaId));
        db.Cekler.RemoveRange(db.Cekler.Where(x => x.FirmaId == firmaId));
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;

        var atlas = CreateCari(firmaId, "Atlas Mimarlık", "Atlas Mimarlık ve Proje Ltd. Şti.", "0532 100 10 01", "1111111111", CariTip.Alici);
        var mavi = CreateCari(firmaId, "Mavi Yapı Dekorasyon", "Mavi Yapı Dekorasyon Ltd. Şti.", "0532 100 10 02", "2222222222", CariTip.Alici);
        var luna = CreateCari(firmaId, "Luna İç Mimarlık", "Luna İç Mimarlık A.Ş.", "0532 100 10 03", "3333333333", CariTip.Alici);
        var kuzey = CreateCari(firmaId, "Kuzey Ofis Sistemleri", "Kuzey Ofis Sistemleri Ltd.", "0532 100 10 04", "4444444444", CariTip.Alici);

        var panelmax = CreateCari(firmaId, "Panelmax MDF", "Panelmax MDF ve Orman Ürünleri", "0532 200 20 01", "5555555555", CariTip.Satici);
        var ege = CreateCari(firmaId, "Ege Aksesuar", "Ege Mobilya Aksesuarları Ltd.", "0532 200 20 02", "6666666666", CariTip.Satici);
        var akdeniz = CreateCari(firmaId, "Akdeniz Orman Ürünleri", "Akdeniz Orman Ürünleri A.Ş.", "0532 200 20 03", "7777777777", CariTip.Satici);

        db.CariKartlar.AddRange(atlas, mavi, luna, kuzey, panelmax, ege, akdeniz);
        await db.SaveChangesAsync();

        var f1 = CreateFatura(firmaId, atlas.Id, "NVM-2026-0001", FaturaTipi.Satis, today.AddDays(-8), today.AddDays(7), 18500m, "Atlas Mimarlık mutfak dolabı projesi",
            CreateKalem("Lake mutfak alt dolap", 1, 18500m, 20m),
            CreateKalem("Tezgah ve aksesuar", 1, 8200m, 20m));

        var f2 = CreateFatura(firmaId, mavi.Id, "NVM-2026-0002", FaturaTipi.Satis, today.AddDays(-6), today.AddDays(14), 0m, "Mavi Yapı vestiyer ve banyo dolabı",
            CreateKalem("Vestiyer dolabı", 1, 14500m, 20m),
            CreateKalem("Banyo dolabı", 2, 6200m, 20m));

        var f3 = CreateFatura(firmaId, luna.Id, "NVM-2026-0003", FaturaTipi.Satis, today.AddDays(-4), today.AddDays(10), 43200m, "Luna İç Mimarlık ofis mobilyaları",
            CreateKalem("Ofis masa takımı", 4, 7800m, 20m),
            CreateKalem("Dosya dolabı", 6, 4200m, 20m));

        var f4 = CreateFatura(firmaId, panelmax.Id, "NVM-2026-0004", FaturaTipi.Alis, today.AddDays(-5), today.AddDays(15), 15000m, "MDF panel alımı",
            CreateKalem("18mm beyaz MDF", 30, 580m, 20m),
            CreateKalem("18mm antrasit MDF", 20, 640m, 20m));

        var f5 = CreateFatura(firmaId, ege.Id, "NVM-2026-0005", FaturaTipi.Alis, today.AddDays(-3), today.AddDays(20), 0m, "Aksesuar ve ray alımı",
            CreateKalem("Çekmece rayı", 25, 180m, 20m),
            CreateKalem("Dolap kulpu", 80, 55m, 20m),
            CreateKalem("Menteşe", 120, 32m, 20m));

        var f6 = CreateFatura(firmaId, kuzey.Id, "NVM-2026-0006", FaturaTipi.Satis, today.AddDays(-2), today.AddDays(12), 12000m, "Kuzey Ofis toplantı odası mobilyası",
            CreateKalem("Toplantı masası", 1, 16500m, 20m),
            CreateKalem("Duvar raf sistemi", 1, 9500m, 20m));

        db.Faturalar.AddRange(f1, f2, f3, f4, f5, f6);
        await db.SaveChangesAsync();

        db.KasaHareketleri.AddRange(
            CreateKasa(firmaId, atlas.Id, f1.Id, today.AddDays(-7), HareketTipi.Giris, 18500m, "Atlas Mimarlık tahsilatı"),
            CreateKasa(firmaId, luna.Id, f3.Id, today.AddDays(-3), HareketTipi.Giris, 43200m, "Luna İç Mimarlık tam tahsilat"),
            CreateKasa(firmaId, kuzey.Id, f6.Id, today.AddDays(-1), HareketTipi.Giris, 12000m, "Kuzey Ofis kısmi tahsilat"),
            CreateKasa(firmaId, panelmax.Id, f4.Id, today.AddDays(-4), HareketTipi.Cikis, 15000m, "Panelmax MDF ödemesi"),
            CreateKasa(firmaId, null, null, today.AddDays(-2), HareketTipi.Cikis, 3800m, "Atölye elektrik faturası"),
            CreateKasa(firmaId, null, null, today.AddDays(-1), HareketTipi.Cikis, 6500m, "Personel maaş ödemesi"),
            CreateKasa(firmaId, null, null, today, HareketTipi.Cikis, 1750m, "Araç yakıt gideri")
        );

        var stoklar = new List<StokUrun>
        {
            new() { FirmaId = firmaId, Ad = "18mm Beyaz MDF", Kod = "STK-001", Birim = "Plaka" },
            new() { FirmaId = firmaId, Ad = "18mm Antrasit MDF", Kod = "STK-002", Birim = "Plaka" },
            new() { FirmaId = firmaId, Ad = "PVC Kenar Bandı Beyaz", Kod = "STK-003", Birim = "Metre" },
            new() { FirmaId = firmaId, Ad = "PVC Kenar Bandı Siyah", Kod = "STK-004", Birim = "Metre" },
            new() { FirmaId = firmaId, Ad = "Çekmece Ray Seti", Kod = "STK-005", Birim = "Takım" },
            new() { FirmaId = firmaId, Ad = "Dolap Kulpu", Kod = "STK-006", Birim = "Adet" },
            new() { FirmaId = firmaId, Ad = "Menteşe", Kod = "STK-007", Birim = "Adet" }
        };

        db.StokUrunler.AddRange(stoklar);
        await db.SaveChangesAsync();

        db.StokHareketleri.AddRange(
            CreateStok(firmaId, stoklar[0], today.AddDays(-10), StokHareketTipi.Giris, 50, 580m, "Panelmax MDF stok girişi"),
            CreateStok(firmaId, stoklar[1], today.AddDays(-9), StokHareketTipi.Giris, 35, 640m, "Panelmax MDF stok girişi"),
            CreateStok(firmaId, stoklar[2], today.AddDays(-8), StokHareketTipi.Giris, 300, 18m, "Kenar bandı alımı"),
            CreateStok(firmaId, stoklar[4], today.AddDays(-7), StokHareketTipi.Giris, 40, 180m, "Ray seti alımı"),
            CreateStok(firmaId, stoklar[5], today.AddDays(-7), StokHareketTipi.Giris, 120, 55m, "Kulp alımı"),
            CreateStok(firmaId, stoklar[0], today.AddDays(-3), StokHareketTipi.Cikis, 8, 580m, "Atlas Mimarlık üretim çıkışı"),
            CreateStok(firmaId, stoklar[4], today.AddDays(-3), StokHareketTipi.Cikis, 6, 180m, "Luna proje üretim çıkışı")
        );

        db.Cekler.AddRange(
            new Cek
            {
                FirmaId = firmaId,
                No = "NVM-CEK-001",
                Tarih = today.AddDays(25),
                Tutar = 22500m,
                Tip = CekTipi.Alinacak,
                Aciklama = "Mavi Yapı alınacak çek",
                OlusturmaTarihi = DateTime.UtcNow
            }
        );

        var ayar = await db.FaturaNumaraAyarlari.FirstOrDefaultAsync(x => x.FirmaId == firmaId);
        if (ayar == null)
        {
            db.FaturaNumaraAyarlari.Add(new FaturaNumaraAyari
            {
                FirmaId = firmaId,
                Prefix = "NVM",
                SonNumara = 6,
                SiraUzunlugu = 4,
                YilEkle = true
            });
        }
        else
        {
            ayar.Prefix = "NVM";
            ayar.SonNumara = 6;
            ayar.SiraUzunlugu = 4;
            ayar.YilEkle = true;
        }

        await db.SaveChangesAsync();
    }

    private static CariKart CreateCari(int firmaId, string ad, string unvan, string telefon, string vergiNo, CariTip tip)
    {
        return new CariKart
        {
            FirmaId = firmaId,
            Ad = ad,
            Unvan = unvan,
            Telefon = telefon,
            VergiNo = vergiNo,
            Tip = tip,
            OlusturmaTarihi = DateTime.UtcNow
        };
    }

    private static Fatura CreateFatura(int firmaId, int cariId, string no, FaturaTipi tip, DateTime tarih, DateTime vade, decimal odenen, string aciklama, params FaturaKalem[] kalemler)
    {
        var fatura = new Fatura
        {
            FirmaId = firmaId,
            CariKartId = cariId,
            FaturaNo = no,
            Tip = tip,
            Tarih = tarih,
            VadeTarihi = vade,
            OdenenToplam = odenen,
            Aciklama = aciklama,
            OlusturmaTarihi = DateTime.UtcNow,
            Kalemler = kalemler.ToList()
        };

        ApplyTotals(fatura);
        return fatura;
    }

    private static KasaHareket CreateKasa(int firmaId, int? cariId, int? faturaId, DateTime tarih, HareketTipi tip, decimal tutar, string aciklama)
    {
        return new KasaHareket
        {
            FirmaId = firmaId,
            CariKartId = cariId,
            FaturaId = faturaId,
            Tarih = tarih,
            Tip = tip,
            Tutar = tutar,
            Aciklama = aciklama
        };
    }

    private static StokHareket CreateStok(int firmaId, StokUrun urun, DateTime tarih, StokHareketTipi tip, decimal miktar, decimal fiyat, string aciklama)
    {
        return new StokHareket
        {
            FirmaId = firmaId,
            StokUrunId = urun.Id,
            Ad = urun.Ad,
            Tarih = tarih,
            Tip = tip,
            Miktar = miktar,
            BirimFiyat = fiyat,
            KdvOrani = 20m,
            Aciklama = aciklama
        };
    }

    private static FaturaKalem CreateKalem(string aciklama, decimal miktar, decimal birimFiyat, decimal kdvOrani)
    {
        var araToplam = miktar * birimFiyat;
        var kdvTutar = araToplam * kdvOrani / 100m;

        return new FaturaKalem
        {
            Aciklama = aciklama,
            Miktar = miktar,
            BirimFiyat = birimFiyat,
            KdvOrani = kdvOrani,
            AraToplam = araToplam,
            KdvTutar = kdvTutar,
            GenelToplam = araToplam + kdvTutar
        };
    }

    private static void ApplyTotals(Fatura fatura)
    {
        fatura.AraToplam = fatura.Kalemler.Sum(x => x.AraToplam);
        fatura.KdvToplam = fatura.Kalemler.Sum(x => x.KdvTutar);
        fatura.GenelToplam = fatura.Kalemler.Sum(x => x.GenelToplam);
    }
}
