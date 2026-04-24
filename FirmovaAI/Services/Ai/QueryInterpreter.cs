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

        if (ContainsAny(lower, "son 10", "son işlemler", "kasa hareketleri", "son kasa hareketleri"))
        {
            result.Intent = "SonKasaHareketleri";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bakiye", "kasa bakiyesi", "kasada ne kadar", "kasada ne kadar var"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "gelir", "giriş", "para girdi", "kasa girişi", "kasaya ne kadar"))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "gider", "çıkış", "para çıktı", "kasa çıkışı", "kasadan ne kadar"))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en borçlu müşteri", "en çok borçlu müşteri", "kim bana en çok borçlu"))
        {
            result.Intent = "EnBorcluMusteri";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en alacaklı satıcı", "en çok alacaklı satıcı", "en çok ödeme yapılan satıcı"))
        {
            result.Intent = "EnAlacakliSatici";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "toplam müşteri tahsilatı", "müşterilerden toplam tahsilat", "toplam tahsilat"))
        {
            result.Intent = "ToplamMusteriTahsilati";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "toplam satıcı ödemesi", "satıcılara toplam ödeme", "toplam ödeme"))
        {
            result.Intent = "ToplamSaticiOdemesi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün kasa çıkışı", "bugün kasadan ne kadar çıktı", "bugün toplam çıkış"))
        {
            result.Intent = "BugunKasaCikis";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün kasa girişi", "bugün kasaya ne kadar para girdi", "bugün toplam giriş"))
        {
            result.Intent = "BugunKasaGiris";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "bugün kasa", "bugün kasa durumu", "kasa durumu"))
        {
            result.Intent = "BugunKasa";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "en son kime avans verildi", "son avans verilen kişi kim", "en son avans kime verildi"))
        {
            result.Intent = "SonAvansVerilenKisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "avans", "maaş avansı", "aldığı avans") &&
            ContainsAny(lower, "toplam", "ne kadar", "kaç tl", "kaç para") &&
            !HasPersonName(text))
        {
            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "avans", "maaş avansı", "aldığı avans"))
        {
            result.CalisanAdi = ExtractPersonName(text);
            result.Intent = "CalisanAvansToplam";
            result.IsSuccess = true;
            return result;
        }
        if (lower.Contains("borcu") && !lower.Contains("en çok"))
        {
            result.Intent = "MusteriBorc";
            result.CalisanAdi = text.Split(' ')[0];
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
        if (ContainsAny(text, "ne kadar", "toplam", "kaç tl", "kaç para", "kaç"))
            return "Total";

        if (ContainsAny(text, "detay", "ayrıntı"))
            return "Detail";

        return "List";
    }

    private static bool HasPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.Trim().ToLowerInvariant();

        if (lower.StartsWith("toplam avans") ||
            lower.StartsWith("bu ay toplam") ||
            lower.StartsWith("bugün toplam") ||
            lower.StartsWith("geçen ay toplam"))
            return false;

        return false;
    }

    private static string? ExtractPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 0)
            return words[0].Trim().ToLowerInvariant();

        return null;
    }
}