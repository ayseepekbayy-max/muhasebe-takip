using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class OdemePlanlamaService
{
    public static DateTime ToUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    public static DateTime AyIcinGecerliGun(DateTime ay, int gun)
    {
        var temizGun = Math.Clamp(gun, 1, 31);
        var sonGun = DateTime.DaysInMonth(ay.Year, ay.Month);
        return new DateTime(ay.Year, ay.Month, Math.Min(temizGun, sonGun), 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime SonrakiAy(DateTime tarih, int gun)
    {
        return AyIcinGecerliGun(tarih.AddMonths(1), gun);
    }

    public static DateTime TahminiSonOdemeTarihi(DateTime ilkOdemeTarihi, int odemeGunu, int toplamTaksitSayisi)
    {
        var ilk = AyIcinGecerliGun(ToUtcDate(ilkOdemeTarihi), odemeGunu);
        var sonAy = ilk.AddMonths(Math.Max(0, toplamTaksitSayisi - 1));
        return AyIcinGecerliGun(sonAy, odemeGunu);
    }

    public static bool TamamlanmisMi(OdemePlani odeme)
    {
        return odeme.TamamlandiMi || odeme.KalanTaksitSayisi <= 0;
    }

    public static OdemeDurumu Durum(OdemePlani odeme, DateTime bugun, bool buAyOdendi)
    {
        if (TamamlanmisMi(odeme))
            return OdemeDurumu.Tamamlandi;

        if (!odeme.AktifMi)
            return OdemeDurumu.Pasif;

        if (buAyOdendi)
            return OdemeDurumu.Odendi;

        if (odeme.SonrakiOdemeTarihi == null)
            return OdemeDurumu.Tamamlandi;

        var sonraki = ToUtcDate(odeme.SonrakiOdemeTarihi.Value);
        if (sonraki < bugun)
            return OdemeDurumu.Gecikti;

        if (sonraki == bugun)
            return OdemeDurumu.Bugun;

        return OdemeDurumu.Yaklasiyor;
    }

    public static string BildirimTuru(OdemePlani odeme, DateTime bugun)
    {
        if (TamamlanmisMi(odeme) || odeme.SonrakiOdemeTarihi == null)
            return "Tamamlanan Ödeme";

        var sonraki = ToUtcDate(odeme.SonrakiOdemeTarihi.Value);
        if (sonraki < bugun)
            return "Geciken Ödeme";

        if (sonraki == bugun)
            return "Bugünkü Ödeme";

        return "Yaklaşan Ödeme";
    }
}
