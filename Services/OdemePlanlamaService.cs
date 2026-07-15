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

    public static OdemeDurumu Durum(OdemePlani odeme, DateTime bugun, bool buAyOdendi)
    {
        if (!odeme.AktifMi)
            return OdemeDurumu.Pasif;

        if (odeme.KalanTaksitSayisi <= 0)
            return OdemeDurumu.Tamamlandi;

        if (buAyOdendi)
            return OdemeDurumu.Odendi;

        var sonraki = ToUtcDate(odeme.SonrakiOdemeTarihi);
        if (sonraki < bugun)
            return OdemeDurumu.Gecikti;

        if (sonraki == bugun)
            return OdemeDurumu.Bugun;

        if (sonraki <= bugun.AddDays(Math.Max(0, odeme.BildirimGunu)))
            return OdemeDurumu.Yaklasiyor;

        return OdemeDurumu.Yaklasiyor;
    }

    public static string BildirimTuru(OdemePlani odeme, DateTime bugun)
    {
        var sonraki = ToUtcDate(odeme.SonrakiOdemeTarihi);
        if (sonraki < bugun)
            return "Geciken Ã–deme";

        if (sonraki == bugun)
            return "BugÃ¼nkÃ¼ Ã–deme";

        return "YaklaÅŸan Ã–deme";
    }
}