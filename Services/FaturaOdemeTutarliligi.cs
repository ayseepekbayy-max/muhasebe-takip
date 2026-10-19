using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public static class FaturaOdemeTutarliligi
{
    // Apply only the edited movement's difference; preserve unrelated and legacy payments.
    public static string? Dogrula(Fatura fatura, KasaHareket hareket, decimal yeniTutar,
        HareketTipi yeniTip, int? yeniCariId)
    {
        if (fatura.FirmaId != hareket.FirmaId || fatura.Id != hareket.FaturaId)
            return "Fatura ile kasa hareketi aynı firmaya ait olmalıdır.";
        var beklenenTip = fatura.Tip == FaturaTipi.Satis ? HareketTipi.Giris : HareketTipi.Cikis;
        if (yeniTip != beklenenTip || yeniCariId != fatura.CariKartId)
            return "Faturaya bağlı hareketin carisi ve giriş/çıkış türü faturayla aynı olmalıdır.";
        var yeniOdenen = fatura.OdenenToplam - hareket.Tutar + yeniTutar;
        if (yeniOdenen < 0)
            return "Faturanın mevcut ödeme toplamı bu hareketle uyumsuz. Önce eski kayıtlar kontrol edilmelidir.";
        if (yeniOdenen > fatura.GenelToplam)
            return "Bu değişiklik faturanın toplam tutarından fazla ödeme oluşturur.";
        return null;
    }

    public static void Uygula(Fatura fatura, decimal eskiTutar, decimal yeniTutar)
    {
        fatura.OdenenToplam += yeniTutar - eskiTutar;
        if (fatura.Durum != FaturaDurumu.Iptal)
            fatura.Durum = FaturaDurumuExtensions.OdemeDurumu(fatura.GenelToplam, fatura.OdenenToplam);
    }
}
