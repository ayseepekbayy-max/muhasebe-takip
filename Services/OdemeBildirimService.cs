using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public class OdemeBildirimService : IOdemeBildirimService
{
    private readonly AppDbContext _db;

    public OdemeBildirimService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<OdemeBildirimSatiri>> AktifBildirimleriGetirAsync(
        int firmaId,
        int? kullaniciId,
        DateTime bugun,
        CancellationToken cancellationToken = default)
    {
        bugun = OdemePlanlamaService.ToUtcDate(bugun);
        var ucGunSonra = bugun.AddDays(3);

        var gizlenenOdemeIds = kullaniciId.HasValue
            ? await _db.OdemeBildirimGizlemeleri
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId &&
                            x.KullaniciId == kullaniciId.Value &&
                            x.GizlemeTarihi == bugun)
                .Select(x => x.OdemePlaniId)
                .ToListAsync(cancellationToken)
            : new List<int>();

        var planlar = await _db.OdemePlanlari
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId &&
                        x.AktifMi &&
                        !x.TamamlandiMi &&
                        x.BildirimAktifMi &&
                        x.KalanTaksitSayisi > 0 &&
                        x.SonrakiOdemeTarihi != null &&
                        x.SonrakiOdemeTarihi.Value.Date <= ucGunSonra)
            .OrderBy(x => x.SonrakiOdemeTarihi)
            .ThenBy(x => x.OdemeAdi)
            .ToListAsync(cancellationToken);

        return planlar
            .Where(x => !gizlenenOdemeIds.Contains(x.Id))
            .Select(x => SatiraDonustur(x, bugun))
            .OrderBy(x => x.Oncelik)
            .ThenBy(x => x.SonOdemeTarihi)
            .ThenBy(x => x.OdemeAdi)
            .ToList();
    }

    public async Task<int> AktifBildirimSayisiAsync(
        int firmaId,
        int? kullaniciId,
        DateTime bugun,
        CancellationToken cancellationToken = default)
    {
        var bildirimler = await AktifBildirimleriGetirAsync(firmaId, kullaniciId, bugun, cancellationToken);
        return bildirimler.Count;
    }

    public async Task BugunGizleAsync(
        int firmaId,
        int kullaniciId,
        string? kullaniciAdi,
        int odemePlaniId,
        DateTime bugun,
        CancellationToken cancellationToken = default)
    {
        bugun = OdemePlanlamaService.ToUtcDate(bugun);

        var odemeVarMi = await _db.OdemePlanlari.AnyAsync(x =>
            x.Id == odemePlaniId &&
            x.FirmaId == firmaId,
            cancellationToken);

        if (!odemeVarMi)
            return;

        var zatenGizliMi = await _db.OdemeBildirimGizlemeleri.AnyAsync(x =>
            x.FirmaId == firmaId &&
            x.KullaniciId == kullaniciId &&
            x.OdemePlaniId == odemePlaniId &&
            x.GizlemeTarihi == bugun,
            cancellationToken);

        if (zatenGizliMi)
            return;

        _db.OdemeBildirimGizlemeleri.Add(new OdemeBildirimGizleme
        {
            FirmaId = firmaId,
            KullaniciId = kullaniciId,
            OdemePlaniId = odemePlaniId,
            GizlemeTarihi = bugun,
            OlusturmaTarihi = DateTime.UtcNow,
            OlusturanKullaniciAdi = kullaniciAdi
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static OdemeBildirimSatiri SatiraDonustur(OdemePlani odeme, DateTime bugun)
    {
        var sonOdemeTarihi = OdemePlanlamaService.ToUtcDate(odeme.SonrakiOdemeTarihi!.Value);
        var kalanGun = (sonOdemeTarihi - bugun).Days;

        return new OdemeBildirimSatiri
        {
            OdemePlaniId = odeme.Id,
            OdemeAdi = odeme.OdemeAdi,
            Tutar = odeme.AylikOdemeTutari,
            SonOdemeTarihi = sonOdemeTarihi,
            KalanGun = kalanGun,
            Oncelik = kalanGun < 0 ? 0 : kalanGun + 1,
            Durum = DurumMetni(kalanGun),
            RenkSinifi = RenkSinifi(kalanGun),
            GunBilgisi = GunBilgisi(kalanGun)
        };
    }

    private static string DurumMetni(int kalanGun) => kalanGun switch
    {
        < 0 => "Gecikti",
        0 => "Bugün",
        1 => "1 Gün",
        2 => "2 Gün",
        3 => "3 Gün",
        _ => "Yaklaşıyor"
    };

    private static string RenkSinifi(int kalanGun) => kalanGun switch
    {
        < 0 => "payment-notice-overdue",
        0 => "payment-notice-today",
        1 => "payment-notice-one",
        2 => "payment-notice-two",
        3 => "payment-notice-three",
        _ => "payment-notice-neutral"
    };

    private static string GunBilgisi(int kalanGun) => kalanGun switch
    {
        < 0 => $"{Math.Abs(kalanGun)} gün gecikti",
        0 => "Bugün ödenecek",
        1 => "1 gün kaldı",
        _ => $"{kalanGun} gün kaldı"
    };
}
