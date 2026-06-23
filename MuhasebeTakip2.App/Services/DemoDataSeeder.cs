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

    public static async Task<Kullanici> PrepareDemoAsync(AppDbContext db)
    {
        var kullanici = await db.Kullanicilar
            .Include(x => x.Firma)
            .FirstOrDefaultAsync(x => x.KullaniciAdi == DemoUserName || x.Email == DemoEmail);

        if (kullanici?.Firma == null)
        {
            var firma = new Firma
            {
                FirmaAdi = "Demo Mobilya Sistemleri",
                AktifMi = true,
                Adres = "Demo Mah. Uretim Cad. No: 10",
                Telefon = "0212 000 00 00",
                Email = "demo@firmova.com",
                VergiDairesi = "Demo Vergi Dairesi",
                VergiNo = "0000000000"
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
            kullanici.Firma.FirmaAdi = "Demo Mobilya Sistemleri";
            await db.SaveChangesAsync();
        }

        await ResetDemoDataAsync(db, kullanici.FirmaId);

        return await db.Kullanicilar
            .Include(x => x.Firma)
            .FirstAsync(x => x.Id == kullanici.Id);
    }

    private static async Task ResetDemoDataAsync(AppDbContext db, int firmaId)
    {
        var faturaIds = await db.Faturalar
            .Where(x => x.FirmaId == firmaId)
            .Select(x => x.Id)
            .ToListAsync();

        db.KasaHareketleri.RemoveRange(db.KasaHareketleri.Where(x => x.FirmaId == firmaId));
        db.FaturaKalemleri.RemoveRange(db.FaturaKalemleri.Where(x => faturaIds.Contains(x.FaturaId)));
        db.Faturalar.RemoveRange(db.Faturalar.Where(x => x.FirmaId == firmaId));
        db.CariKartlar.RemoveRange(db.CariKartlar.Where(x => x.FirmaId == firmaId));
        db.StokHareketleri.RemoveRange(db.StokHareketleri.Where(x => x.FirmaId == firmaId));
        db.StokUrunler.RemoveRange(db.StokUrunler.Where(x => x.FirmaId == firmaId));
        db.Cekler.RemoveRange(db.Cekler.Where(x => x.FirmaId == firmaId));
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        var alici = new CariKart
        {
            FirmaId = firmaId,
            Ad = "ANK Ofis Proje",
            Unvan = "ANK Ofis Proje Ltd.",
            Telefon = "0532 000 00 01",
            VergiNo = "1234567890",
            Tip = CariTip.Alici,
            OlusturmaTarihi = DateTime.UtcNow
        };
        var satici = new CariKart
        {
            FirmaId = firmaId,
            Ad = "Panel Tedarik",
            Unvan = "Panel Tedarik A.S.",
            Telefon = "0532 000 00 02",
            VergiNo = "9876543210",
            Tip = CariTip.Satici,
            OlusturmaTarihi = DateTime.UtcNow
        };
        var musteri = new CariKart
        {
            FirmaId = firmaId,
            Ad = "Mavi Dekorasyon",
            Unvan = "Mavi Dekorasyon",
            Telefon = "0532 000 00 03",
            VergiNo = "1122334455",
            Tip = CariTip.Alici,
            OlusturmaTarihi = DateTime.UtcNow
        };

        db.CariKartlar.AddRange(alici, satici, musteri);
        await db.SaveChangesAsync();

        var satisFaturasi = new Fatura
        {
            FirmaId = firmaId,
            CariKartId = alici.Id,
            FaturaNo = "DMO-2026-0001",
            Tip = FaturaTipi.Satis,
            Tarih = today.AddDays(-4),
            VadeTarihi = today.AddDays(10),
            OdenenToplam = 2500m,
            Aciklama = "Demo satis faturasi",
            OlusturmaTarihi = DateTime.UtcNow,
            Kalemler = new List<FaturaKalem>
            {
                CreateKalem("Mutfak dolabi montaji", 1, 12500m, 20m),
                CreateKalem("Nakliye ve kurulum", 1, 1500m, 20m)
            }
        };
        ApplyTotals(satisFaturasi);

        var alisFaturasi = new Fatura
        {
            FirmaId = firmaId,
            CariKartId = satici.Id,
            FaturaNo = "DMO-2026-0002",
            Tip = FaturaTipi.Alis,
            Tarih = today.AddDays(-2),
            VadeTarihi = today.AddDays(15),
            OdenenToplam = 0m,
            Aciklama = "Demo alis faturasi",
            OlusturmaTarihi = DateTime.UtcNow,
            Kalemler = new List<FaturaKalem>
            {
                CreateKalem("MDF panel", 20, 420m, 20m),
                CreateKalem("Kenar bandi", 12, 90m, 20m)
            }
        };
        ApplyTotals(alisFaturasi);

        db.Faturalar.AddRange(satisFaturasi, alisFaturasi);
        await db.SaveChangesAsync();

        db.KasaHareketleri.AddRange(
            new KasaHareket
            {
                FirmaId = firmaId,
                CariKartId = alici.Id,
                FaturaId = satisFaturasi.Id,
                Tarih = today.AddDays(-3),
                Tip = HareketTipi.Giris,
                Tutar = 2500m,
                Aciklama = "Demo tahsilat - DMO-2026-0001"
            },
            new KasaHareket
            {
                FirmaId = firmaId,
                Tarih = today.AddDays(-1),
                Tip = HareketTipi.Cikis,
                Tutar = 850m,
                Aciklama = "Demo genel gider"
            });

        var masa = new StokUrun { FirmaId = firmaId, Ad = "L masa", Kod = "STK-001", Birim = "Adet" };
        var dolap = new StokUrun { FirmaId = firmaId, Ad = "Dosya dolabi", Kod = "STK-002", Birim = "Adet" };
        db.StokUrunler.AddRange(masa, dolap);
        await db.SaveChangesAsync();

        db.StokHareketleri.AddRange(
            new StokHareket
            {
                FirmaId = firmaId,
                StokUrunId = masa.Id,
                Ad = masa.Ad,
                Tarih = today.AddDays(-6),
                Tip = StokHareketTipi.Giris,
                Miktar = 8,
                BirimFiyat = 3500m,
                KdvOrani = 20m,
                Aciklama = "Demo stok girisi"
            },
            new StokHareket
            {
                FirmaId = firmaId,
                StokUrunId = dolap.Id,
                Ad = dolap.Ad,
                Tarih = today.AddDays(-5),
                Tip = StokHareketTipi.Giris,
                Miktar = 5,
                BirimFiyat = 2200m,
                KdvOrani = 20m,
                Aciklama = "Demo stok girisi"
            });

        db.Cekler.Add(new Cek
        {
            FirmaId = firmaId,
            No = "DMO-CEK-001",
            Tarih = today.AddDays(20),
            Tutar = 7500m,
            Tip = CekTipi.Alinacak,
            Aciklama = "Demo alinacak cek",
            OlusturmaTarihi = DateTime.UtcNow
        });

        var ayar = await db.FaturaNumaraAyarlari.FirstOrDefaultAsync(x => x.FirmaId == firmaId);
        if (ayar == null)
        {
            db.FaturaNumaraAyarlari.Add(new FaturaNumaraAyari
            {
                FirmaId = firmaId,
                Prefix = "DMO",
                SonNumara = 2,
                SiraUzunlugu = 4,
                YilEkle = true
            });
        }
        else
        {
            ayar.Prefix = "DMO";
            ayar.SonNumara = 2;
            ayar.SiraUzunlugu = 4;
            ayar.YilEkle = true;
        }

        await db.SaveChangesAsync();
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
