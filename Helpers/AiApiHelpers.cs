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
            .Where(x => x.Tip == CalisanHareketTipi.Avans && !x.ArsivlendiMi)
            .AsQueryable();

        query = ApplyDateFilter(query, dateRange);

        var liste = await query
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        var tumCalisanlar = liste
            .Select(x => x.Calisan?.AdSoyad)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var bulunanAd = FindBestEmployeeName(calisanAdi, tumCalisanlar);

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

        var filtered = liste
            .Where(x => string.Equals(x.Calisan?.AdSoyad, bulunanAd, StringComparison.OrdinalIgnoreCase))
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

    public static async Task<CalisanAvansToplamResponse> GetToplamAvansAsync(
        AppDbContext db,
        string? dateRange)
    {
        var query = db.CalisanAvanslari
            .Include(x => x.Calisan)
            .Where(x => x.Tip == CalisanHareketTipi.Avans && !x.ArsivlendiMi)
            .AsQueryable();

        query = ApplyDateFilter(query, dateRange);

        var toplam = await query.SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return new CalisanAvansToplamResponse
        {
            Success = true,
            EmployeeName = "",
            Total = toplam,
            Message = $"Bu ay toplam avans: {toplam:N2} TL"
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetSonAvansVerilenKisiAsync(AppDbContext db)
    {
        var kayit = await db.CalisanAvanslari
            .Include(x => x.Calisan)
            .Where(x => x.Tip == CalisanHareketTipi.Avans && !x.ArsivlendiMi)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (kayit == null)
        {
            return new CalisanAvansToplamResponse
            {
                Success = true,
                EmployeeName = "",
                Total = 0,
                Message = "Hiç avans kaydı bulunamadı."
            };
        }

        var ad = kayit.Calisan?.AdSoyad ?? kayit.Ad ?? "Bilinmiyor";

        return new CalisanAvansToplamResponse
        {
            Success = true,
            EmployeeName = ad,
            Total = kayit.Tutar,
            Message = $"En son avans verilen kişi: {ad} - {kayit.Tutar:N2} TL ({kayit.Tarih:dd.MM.yyyy})"
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetBugunKasaDurumuAsync(AppDbContext db, string? kasaIntent)
    {
        var bugun = DateTime.UtcNow.Date;
        var yarin = bugun.AddDays(1);

        var giris = await db.KasaHareketleri
            .Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var cikis = await db.KasaHareketleri
            .Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return kasaIntent switch
        {
            "BugunKasaGiris" => new CalisanAvansToplamResponse
            {
                Success = true,
                Message = $"Bugün kasa girişi: {giris:N2} TL"
            },

            "BugunKasaCikis" => new CalisanAvansToplamResponse
            {
                Success = true,
                Message = $"Bugün kasa çıkışı: {cikis:N2} TL"
            },

            _ => new CalisanAvansToplamResponse
            {
                Success = true,
                Message = $"Bugün kasa girişi: {giris:N2} TL | kasa çıkışı: {cikis:N2} TL"
            }
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetEnBorcluMusteriAsync(AppDbContext db)
    {
        var sonuc = await db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.CariKartId != null && x.CariKart != null && x.CariKart.Tip == CariTip.Alici)
            .GroupBy(x => new { x.CariKartId, x.CariKart!.Unvan, x.CariKart.Ad })
            .Select(g => new
            {
                Ad = !string.IsNullOrWhiteSpace(g.Key.Unvan) ? g.Key.Unvan : g.Key.Ad,
                Net = g.Sum(x => x.Tip == HareketTipi.Giris ? x.Tutar : -x.Tutar)
            })
            .OrderByDescending(x => x.Net)
            .FirstOrDefaultAsync();

        if (sonuc == null)
        {
            return new CalisanAvansToplamResponse
            {
                Success = true,
                Message = "Cariye bağlı müşteri hareketi bulunamadı."
            };
        }

        return new CalisanAvansToplamResponse
        {
            Success = true,
            EmployeeName = sonuc.Ad,
            Total = sonuc.Net,
            Message = $"En borçlu müşteri: {sonuc.Ad} - {sonuc.Net:N2} TL"
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetEnAlacakliSaticiAsync(AppDbContext db)
    {
        var sonuc = await db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.CariKartId != null && x.CariKart != null && x.CariKart.Tip == CariTip.Satici)
            .GroupBy(x => new { x.CariKartId, x.CariKart!.Unvan, x.CariKart.Ad })
            .Select(g => new
            {
                Ad = !string.IsNullOrWhiteSpace(g.Key.Unvan) ? g.Key.Unvan : g.Key.Ad,
                Net = g.Sum(x => x.Tip == HareketTipi.Cikis ? x.Tutar : -x.Tutar)
            })
            .OrderByDescending(x => x.Net)
            .FirstOrDefaultAsync();

        if (sonuc == null)
        {
            return new CalisanAvansToplamResponse
            {
                Success = true,
                Message = "Cariye bağlı satıcı hareketi bulunamadı."
            };
        }

        return new CalisanAvansToplamResponse
        {
            Success = true,
            EmployeeName = sonuc.Ad,
            Total = sonuc.Net,
            Message = $"En alacaklı satıcı: {sonuc.Ad} - {sonuc.Net:N2} TL"
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetToplamMusteriTahsilatiAsync(AppDbContext db, string? dateRange)
    {
        var query = db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.CariKartId != null && x.CariKart != null &&
                        x.CariKart.Tip == CariTip.Alici &&
                        x.Tip == HareketTipi.Giris)
            .AsQueryable();

        query = ApplyKasaDateFilter(query, dateRange);

        var toplam = await query.SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = $"Toplam müşteri tahsilatı: {toplam:N2} TL"
        };
    }

    public static async Task<CalisanAvansToplamResponse> GetToplamSaticiOdemesiAsync(AppDbContext db, string? dateRange)
    {
        var query = db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.CariKartId != null && x.CariKart != null &&
                        x.CariKart.Tip == CariTip.Satici &&
                        x.Tip == HareketTipi.Cikis)
            .AsQueryable();

        query = ApplyKasaDateFilter(query, dateRange);

        var toplam = await query.SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = $"Toplam satıcı ödemesi: {toplam:N2} TL"
        };
    }

    private static IQueryable<CalisanAvans> ApplyDateFilter(IQueryable<CalisanAvans> query, string? dateRange)
    {
        var now = DateTime.UtcNow;

        switch (dateRange)
        {
            case "Today":
            {
                var start = now.Date;
                var end = start.AddDays(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }

            case "ThisMonth":
            {
                var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }

            case "LastMonth":
            {
                var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
                var end = start.AddMonths(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }
        }

        return query;
    }

    private static IQueryable<KasaHareket> ApplyKasaDateFilter(IQueryable<KasaHareket> query, string? dateRange)
    {
        var now = DateTime.UtcNow;

        switch (dateRange)
        {
            case "Today":
            {
                var start = now.Date;
                var end = start.AddDays(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }

            case "ThisMonth":
            {
                var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }

            case "LastMonth":
            {
                var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
                var end = start.AddMonths(1);
                query = query.Where(x => x.Tarih >= start && x.Tarih < end);
                break;
            }
        }

        return query;
    }

    private static string? FindBestEmployeeName(string rawInput, List<string?> employeeNames)
    {
        var input = NormalizeText(rawInput);

        var adaylar = employeeNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new
            {
                Original = x!,
                Full = NormalizeText(x!),
                First = NormalizeText(x!).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""
            })
            .ToList();

        var exactFirstNameMatches = adaylar
            .Where(x => x.First == input)
            .Select(x => x.Original)
            .Distinct()
            .ToList();

        if (exactFirstNameMatches.Count == 1)
            return exactFirstNameMatches[0];

        if (exactFirstNameMatches.Count > 1)
            return null;

        var startsWithMatches = adaylar
            .Where(x => x.First.StartsWith(input) || input.StartsWith(x.First))
            .Select(x => x.Original)
            .Distinct()
            .ToList();

        if (startsWithMatches.Count == 1)
            return startsWithMatches[0];

        if (startsWithMatches.Count > 1)
            return null;

        var fullContainsMatches = adaylar
            .Where(x => x.Full.Contains(input) || input.Contains(x.Full))
            .Select(x => x.Original)
            .Distinct()
            .ToList();

        if (fullContainsMatches.Count == 1)
            return fullContainsMatches[0];

        if (fullContainsMatches.Count > 1)
            return null;

        var scored = adaylar
            .Select(x => new
            {
                x.Original,
                Score = CalculateScore(input, x.First, x.Full)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (!scored.Any())
            return null;

        if (scored.Count == 1)
            return scored[0].Original;

        if (scored[0].Score >= scored[1].Score + 100)
            return scored[0].Original;

        return null;
    }

    private static int CalculateScore(string input, string firstName, string fullName)
    {
        int score = 0;

        if (firstName == input)
            score += 1000;

        if (fullName == input)
            score += 900;

        if (firstName.StartsWith(input))
            score += 700;

        if (fullName.StartsWith(input))
            score += 500;

        if (firstName.Contains(input))
            score += 300;

        if (fullName.Contains(input))
            score += 200;

        var firstDistance = LevenshteinDistance(firstName, input);
        if (firstDistance <= 1)
            score += 150;

        return score;
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
