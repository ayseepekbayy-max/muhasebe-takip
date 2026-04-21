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

            if (ContainsAny(lower, "ne kadar", "toplam", "kaç tl", "kaç para", "kaç"))
                result.Intent = "CalisanAvansToplam";
            else
                result.Intent = "CalisanAvansListele";

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

        text = text.Trim();
        var lower = text.ToLowerInvariant();

        var keywords = new[]
        {
            "bu ay", "bugün", "geçen ay", "avans", "borç", "maaş",
            "ne kadar", "kaç tl", "kaç para", "kaç", "göster", "listele", "toplam"
        };

        foreach (var keyword in keywords)
        {
            var index = lower.IndexOf(keyword);
            if (index > 0)
            {
                var before = text[..index].Trim();
                return !string.IsNullOrWhiteSpace(before);
            }
        }

        return false;
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