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

        if (ContainsAny(lower, "avans", "maaş avansı", "aldığı avans"))
        {
            result.CalisanAdi = ExtractPersonName(text);

            if (ContainsAny(lower, "ne kadar", "toplam", "kaç tl", "kaç para", "kaç"))
                result.Intent = "CalisanAvansToplam";
            else
                result.Intent = "CalisanAvansListele";

            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "kasa", "kasada", "nakit"))
        {
            if (ContainsAny(lower, "giriş", "giren", "tahsilat"))
                result.Intent = "KasaGirisToplam";
            else if (ContainsAny(lower, "çıkış", "çıkan", "ödeme", "gider"))
                result.Intent = "KasaCikisToplam";
            else
                result.Intent = "KasaDurumu";

            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "stok", "ürün", "malzeme"))
        {
            if (ContainsAny(lower, "azalan", "az", "bitmek üzere", "bitiyor"))
                result.Intent = "AzalanStoklar";
            else
                result.Intent = "StokListele";

            result.IsSuccess = true;
            return result;
        }

        if (ContainsAny(lower, "borç", "alacak", "cari"))
        {
            result.CariAdi = ExtractPersonName(text);
            result.Intent = "CariDurumu";
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

        if (ContainsAny(text, "bu ay", "bu ayki"))
            return "ThisMonth";

        if (ContainsAny(text, "geçen ay", "önceki ay"))
            return "LastMonth";

        return "All";
    }

    private static string DetectRequestType(string text)
    {
        if (ContainsAny(text, "ne kadar", "toplam", "kaç tl", "kaç para", "kaç"))
            return "Total";

        if (ContainsAny(text, "detay", "ayrıntı"))
            return "Detail";

        return "List";
    }

    private static string? ExtractPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        var lower = text.ToLowerInvariant();

        var keywords = new[]
        {
            "bu ay", "bugün", "geçen ay", "avans", "borç", "maaş",
            "ne kadar", "kaç tl", "kaç para", "kaç", "göster", "listele"
        };

        foreach (var keyword in keywords)
        {
            var index = lower.IndexOf(keyword);
            if (index > 0)
            {
                var before = text[..index].Trim();

                if (!string.IsNullOrWhiteSpace(before))
                {
                    before = before
                        .Replace("'", "")
                        .Replace("’", "")
                        .Trim();

                    var parts = before.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length > 0)
                        return parts[0].Trim().ToLowerInvariant();
                }
            }
        }

        return null;
    }
}