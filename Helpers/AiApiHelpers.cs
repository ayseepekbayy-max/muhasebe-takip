using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace MuhasebeTakip2.App.Helpers;

public static class AiApiHelpers
{
    public static async Task<CalisanAvansToplamResponse> GetCalisanAvansToplamAsync(
        AppDbContext db,
        string? calisanAdi,
        string? dateRange)
    {
        if (string.IsNullOrWhiteSpace(calisanAdi))
        {
            return new CalisanAvansToplamResponse
            {
                Success = false,
                Message = "Çalışan adı boş olamaz."
            };
        }

        var query = db.CalisanAvanslari
            .Include(x => x.Calisan)
            .Where(x => x.Tip == CalisanHareketTipi.Avans)
            .AsQueryable();

        query = ApplyDateFilter(query, dateRange);

        var liste = await query
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        // 1. Tüm çalışan isimlerini çek
var tumCalisanlar = liste
    .Select(x => x.Calisan?.AdSoyad)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct()
    .ToList();

// 2. En doğru çalışanı bul
var bulunanAd = tumCalisanlar
    .FirstOrDefault(x => NormalizeText(x!) == NormalizeText(calisanAdi));

if (string.IsNullOrWhiteSpace(bulunanAd))
{
    return new CalisanAvansToplamResponse
    {
        Success = true,
        EmployeeName = calisanAdi,
        Total = 0,
        Message = $"{calisanAdi} için kayıt bulunamadı."
    };
}

// 3. SADECE o çalışanın verisini al
var filtered = liste
    .Where(x => x.Calisan?.AdSoyad == bulunanAd)
    .ToList();

var toplam = filtered.Sum(x => x.Tutar);

        return new CalisanAvansToplamResponse
        {
            Success = true,
            EmployeeName = bulunanAd,
            Total = toplam,
            Message = $"{bulunanAd} bu ay toplam {toplam:N2} TL avans aldı"
        };
    }

    private static IQueryable<CalisanAvans> ApplyDateFilter(IQueryable<CalisanAvans> query, string? dateRange)
{
    var today = DateTime.UtcNow;

    switch (dateRange)
    {
        case "Today":
        {
            var start = today.Date;
            var end = start.AddDays(1);

            query = query.Where(x => x.Tarih >= start && x.Tarih < end);
            break;
        }

        case "ThisMonth":
        {
            var start = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);

            query = query.Where(x => x.Tarih >= start && x.Tarih < end);
            break;
        }

        case "LastMonth":
        {
            var start = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
            var end = start.AddMonths(1);

            query = query.Where(x => x.Tarih >= start && x.Tarih < end);
            break;
        }
    }

    return query;
}

    private static bool IsNameMatch(string rawInput, string? fullName, string? shortName)
    {
        var input = NormalizeText(rawInput);
        var full = NormalizeText(fullName ?? "");
        var shortn = NormalizeText(shortName ?? "");

        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (!string.IsNullOrWhiteSpace(full))
        {
            var first = full.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

            if (full.Contains(input) || input.Contains(full))
                return true;

            if (!string.IsNullOrWhiteSpace(first))
            {
                if (first.Contains(input) || input.Contains(first))
                    return true;

                if (LevenshteinDistance(first, input) <= 2)
                    return true;
            }

            if (LevenshteinDistance(full, input) <= 3)
                return true;
        }

        if (!string.IsNullOrWhiteSpace(shortn))
        {
            if (shortn.Contains(input) || input.Contains(shortn))
                return true;

            if (LevenshteinDistance(shortn, input) <= 2)
                return true;
        }

        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Trim().ToLowerInvariant();

        text = text
            .Replace("ç", "c")
            .Replace("ğ", "g")
            .Replace("ı", "i")
            .Replace("i̇", "i")
            .Replace("ö", "o")
            .Replace("ş", "s")
            .Replace("ü", "u")
            .Replace("'", "")
            .Replace("’", "");

        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s))
            return t?.Length ?? 0;

        if (string.IsNullOrEmpty(t))
            return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[s.Length, t.Length];
    }
}

public class CalisanAvansToplamRequest
{
    public string CalisanAdi { get; set; } = "";
    public string DateRange { get; set; } = "ThisMonth";
}

public class CalisanAvansToplamResponse
{
    public bool Success { get; set; }
    public string EmployeeName { get; set; } = "";
    public decimal Total { get; set; }
    public string Message { get; set; } = "";
}