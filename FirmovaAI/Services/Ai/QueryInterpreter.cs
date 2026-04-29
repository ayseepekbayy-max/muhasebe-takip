using FirmovaAI.Models.Ai;

namespace FirmovaAI.Services.Ai;

public class QueryInterpreter
{
    public QueryIntent Interpret(string text)
    {
        text = (text ?? "").Trim();
        var lower = text.ToLowerInvariant();

        var result = new QueryIntent
        {
            RawText = text,
            IsSuccess = false,
            DateRange = DetectDateRange(lower),
            RequestType = DetectRequestType(lower)
        };

        // GENEL ÖZET / DURUM
        if (ContainsAny(lower,
        "genel durum", "durum nasıl", "genel özet", "özet ver",
        "firma durumu", "işletme durumu", "şirket durumu",
        "işler nasıl gidiyor", "işler iyi mi gidiyor",
        "şirket nasıl gidiyor", "firma nasıl",
        "durumumuz nasıl", "işler ne durumda",
        "kasa iyi mi kötü mü", "param artıyor mu azalıyor mu",
        "durum kötü mü", "işler iyi mi"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            return result;
        }

        // SAYISAL GENEL SORGULAR
        if (ContainsAny(lower, "kaç müşteri", "müşteri sayısı", "müşterim var", "toplam müşteri"))
        {
            result.Intent = "MusteriSayisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "kaç çalışan", "çalışan sayısı", "personel sayısı", "kaç personel", "toplam çalışan"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "kaç cari", "cari sayısı", "toplam cari"))
        {
            result.Intent = "CariSayisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "kaç alıcı", "alıcı sayısı", "toplam alıcı"))
        {
            result.Intent = "AliciSayisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "kaç satıcı", "satıcı sayısı", "toplam satıcı"))
        {
            result.Intent = "SaticiSayisi";
            result.IsSuccess = true;
            return result;
        }

        // STOK
        if (ContainsAny(lower, "stokta kaç ürün", "ürün sayısı", "stok ürün sayısı", "kaç ürün var", "toplam ürün", "stok sayısı"))
        {
            result.Intent = "StokSayisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower,
            "biten stok", "stokta biten", "biten ürün", "stok bitti",
            "stokta olmayan", "tükenen ürün", "hangi ürün bitmiş",
            "hangi ürünler bitmiş", "stokta kalmayan"))
        {
            result.Intent = "BitenStoklar";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en çok stok", "stokta en çok", "en fazla stok", "en fazla ürün", "en çok olan ürün"))
        {
            result.Intent = "EnCokStoktaOlanUrun";
            result.IsSuccess = true;
            return result;
        }

        // SON AVANS
        if (ContainsAny(lower, "son", "en son") && ContainsAny(lower, "avans"))
        {
            result.Intent = "SonAvansVerilenKisi";
            result.IsSuccess = true;
            return result;
        }

        // ÇALIŞAN BAZLI AVANS - TOPLAM AVANSTAN ÖNCE OLMALI
        if (ContainsAny(lower, "avans"))
        {
            var ad = ExtractFirstWord(text);

            if (!string.IsNullOrWhiteSpace(ad) &&
                !ContainsAny(lower, "toplam avans", "toplam", "herkes", "tüm çalışan", "bütün çalışan"))
            {
                result.CalisanAdi = ad;
                result.Intent = "CalisanAvansToplam";
                result.IsSuccess = true;
                return result;
            }

            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            return result;
        }

        // KASA HAREKETLERİ
        if (ContainsAny(lower, "son", "son 10", "son işlemler") &&
            ContainsAny(lower, "kasa", "hareket"))
        {
            result.Intent = "SonKasaHareketleri";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün kaç işlem", "bugün kasa işlem", "bugün kaç kasa hareketi", "bugün kaç hareket"))
        {
            result.Intent = "BugunKasaIslemSayisi";
            result.IsSuccess = true;
            return result;
        }

        // BUGÜN KASA
        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "giriş", "gelir", "para girdi"))
        {
            result.Intent = "BugunKasaGiris";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "çıkış", "gider", "para çıktı"))
        {
            result.Intent = "BugunKasaCikis";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "kasa"))
        {
            result.Intent = "BugunKasa";
            result.IsSuccess = true;
            return result;
        }

        // KASA BAKİYE
        if ((ContainsAny(lower, "kasa") &&
             ContainsAny(lower, "ne kadar", "kaç", "para", "bakiye", "durum", "var mı")) ||
            ContainsAny(lower, "kasada kaç", "kasada ne kadar", "kasada para"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            return result;
        }

        // GELİR / GİDER
        if (ContainsAny(lower, "gelir", "giriş", "kazanç", "tahsilat") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "girdi")))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "gider", "çıkış", "masraf") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "çıktı")))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            return result;
        }

        // MÜŞTERİ / SATICI
        if (ContainsAny(lower, "müşteri tahsilatı", "müşterilerden", "müşteriden ne kadar", "toplam tahsilat"))
        {
            result.Intent = "ToplamMusteriTahsilati";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "satıcı ödemesi", "satıcılara", "satıcıya ne kadar", "toplam ödeme"))
        {
            result.Intent = "ToplamSaticiOdemesi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en borçlu", "en çok borçlu", "kim bana en çok borçlu"))
        {
            result.Intent = "EnBorcluMusteri";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en alacaklı", "en çok alacaklı", "en çok ödeme yapılan"))
        {
            result.Intent = "EnAlacakliSatici";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "borç", "borcu", "borçlu"))
        {
            result.CalisanAdi = ExtractFirstWord(text);
            result.Intent = "MusteriBorc";
            result.IsSuccess = true;
            return result;
        }

        result.ErrorMessage = "Soru anlaşılamadı.";
        return result;
    }

    private static bool ContainsAny(string text, params string[] words)
    {
        return words.Any(w => text.Contains(w));
    }

    private static string DetectDateRange(string text)
    {
        if (ContainsAny(text, "bugün", "bugünkü"))
            return "Today";

        if (ContainsAny(text, "geçen ay", "önceki ay"))
            return "LastMonth";

        return "ThisMonth";
    }

    private static string DetectRequestType(string text)
    {
        if (ContainsAny(text, "ne kadar", "toplam", "kaç", "kaç tl", "kaç para"))
            return "Total";

        if (ContainsAny(text, "detay", "ayrıntı", "liste", "listele"))
            return "Detail";

        return "List";
    }

    private static string? ExtractFirstWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length > 0 ? words[0].ToLowerInvariant() : null;
    }
}