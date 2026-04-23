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

        // =========================
        // 🔥 YENİ AKILLI KASA SİSTEMİ
        // =========================

        // GELİR (giriş)
        if (ContainsAny(lower, "gelir", "giriş", "para girdi", "kasa girişi"))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            return result;
        }

        // GİDER (çıkış)
        if (ContainsAny(lower, "gider", "çıkış", "para çıktı", "kasa çıkışı"))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            return result;
        }

        // BAKİYE
        if (ContainsAny(lower, "bakiye", "kasa ne kadar", "kasada ne kadar var"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // CARİ / MÜŞTERİ / SATICI
        // =========================

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

        // =========================
        // AVANS
        // =========================

        if (ContainsAny(lower, "en son kime avans verildi", "son avans verilen kişi kim"))
        {
            result.Intent = "SonAvansVerilenKisi";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "avans") &&
            ContainsAny(lower, "toplam", "ne kadar") &&
            !HasPersonName(text))
        {
            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "avans"))
        {
            result.CalisanAdi = ExtractPersonName(text);
            result.Intent = "CalisanAvansToplam";
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
        if (ContainsAny(text, "bugün"))
            return "Today";

        if (ContainsAny(text, "bu ay"))
            return "ThisMonth";

        if (ContainsAny(text, "geçen ay"))
            return "LastMonth";

        return "ThisMonth";
    }

    private static string DetectRequestType(string text)
    {
        if (ContainsAny(text, "ne kadar", "toplam"))
            return "Total";

        return "List";
    }

    private static bool HasPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length >= 2;
    }

    private static string? ExtractPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 0)
            return words[0].ToLower();

        return null;
    }
}