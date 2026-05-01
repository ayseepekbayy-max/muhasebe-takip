using System.Net;
using System.Net.Http.Json;

namespace FirmovaAI.Services.Ai;

public class MuhasebeApiClient
{
    private readonly HttpClient _httpClient;

    public MuhasebeApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // =======================
    // GENEL METHODLAR
    // =======================

    private async Task<CalisanAvansApiResponse> PostEmptyAsync(string url)
    {
        try
        {
            var response = await _httpClient.PostAsync(url, null);
            return await HandleResponseAsync(response, url);
        }
        catch (Exception ex)
        {
            return Error($"Muhasebe API bağlantı hatası: {ex.Message}");
        }
    }

    private async Task<CalisanAvansApiResponse> PostJsonAsync(string url, CalisanAvansApiRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request);
            return await HandleResponseAsync(response, url);
        }
        catch (Exception ex)
        {
            return Error($"Muhasebe API bağlantı hatası: {ex.Message}");
        }
    }

    private static async Task<CalisanAvansApiResponse> HandleResponseAsync(HttpResponseMessage response, string url)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Error($"Bu özellik henüz bağlı değil: {url}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var detay = await response.Content.ReadAsStringAsync();
            return Error($"API hata verdi ({(int)response.StatusCode}): {detay}");
        }

        return await ReadResponseAsync(response);
    }

    private static async Task<CalisanAvansApiResponse> ReadResponseAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<CalisanAvansApiResponse>();

        return result ?? new CalisanAvansApiResponse
        {
            Success = false,
            Message = "API cevabı okunamadı."
        };
    }

    private static CalisanAvansApiResponse Error(string message)
    {
        return new CalisanAvansApiResponse
        {
            Success = false,
            Message = message
        };
    }

    // =======================
    // API ÇAĞRILARI
    // =======================

    public async Task<CalisanAvansApiResponse> GetMusteriSayisiAsync()
        => await PostEmptyAsync("/api/ai/musteri-sayisi");

    public async Task<CalisanAvansApiResponse> GetCalisanSayisiAsync()
        => await PostEmptyAsync("/api/ai/calisan-sayisi");

    public async Task<CalisanAvansApiResponse> GetCariSayisiAsync()
        => await PostEmptyAsync("/api/ai/cari-sayisi");

    public async Task<CalisanAvansApiResponse> GetAliciSayisiAsync()
        => await PostEmptyAsync("/api/ai/alici-sayisi");

    public async Task<CalisanAvansApiResponse> GetSaticiSayisiAsync()
        => await PostEmptyAsync("/api/ai/satici-sayisi");

    public async Task<CalisanAvansApiResponse> GetStokSayisiAsync()
        => await PostEmptyAsync("/api/ai/stok-sayisi");

    public async Task<CalisanAvansApiResponse> GetBitenStoklarAsync()
        => await PostEmptyAsync("/api/ai/biten-stoklar");

    public async Task<CalisanAvansApiResponse> GetEnCokStoktaOlanUrunAsync()
        => await PostEmptyAsync("/api/ai/en-cok-stokta-olan-urun");

    public async Task<CalisanAvansApiResponse> GetBugunKasaIslemSayisiAsync()
        => await PostEmptyAsync("/api/ai/bugun-kasa-islem-sayisi");

    public async Task<CalisanAvansApiResponse> GetGenelOzetAsync()
        => await PostEmptyAsync("/api/ai/genel-ozet");

    public async Task<CalisanAvansApiResponse> GetSonKasaHareketleriAsync()
        => await PostEmptyAsync("/api/ai/son-kasa-hareketleri");

    public async Task<CalisanAvansApiResponse> GetSonAvansVerilenKisiAsync()
        => await PostEmptyAsync("/api/ai/son-avans-verilen-kisi");

    public async Task<CalisanAvansApiResponse> GetEnBorcluMusteriAsync()
        => await PostEmptyAsync("/api/ai/en-borclu-musteri");

    public async Task<CalisanAvansApiResponse> GetEnAlacakliSaticiAsync()
        => await PostEmptyAsync("/api/ai/en-alacakli-satici");

    public async Task<CalisanAvansApiResponse> GetKarDurumuAsync()
        => await PostEmptyAsync("/api/ai/kar-durumu");

    public async Task<CalisanAvansApiResponse> GetAylikKarsilastirmaAsync()
        => await PostEmptyAsync("/api/ai/aylik-karsilastirma");

    public async Task<CalisanAvansApiResponse> GetEnCokGiderAsync()
        => await PostEmptyAsync("/api/ai/en-cok-gider");

    public async Task<CalisanAvansApiResponse> GetEnCokKazandiranMusteriAsync()
        => await PostEmptyAsync("/api/ai/en-cok-kazandiran-musteri");

    public async Task<CalisanAvansApiResponse> GetStokDurumuAsync()
        => await PostEmptyAsync("/api/ai/stok-durumu");

    public async Task<CalisanAvansApiResponse> GetMaasOdemeKontrolAsync(int? year, int? month)
    => await PostJsonAsync("/api/ai/maas-odeme-kontrol",
        new CalisanAvansApiRequest
        {
            Year = year,
            Month = month
        });

    public async Task<CalisanAvansApiResponse> GetMusteriBorcAsync(string ad)
        => await PostJsonAsync("/api/ai/musteri-borc", new CalisanAvansApiRequest { CalisanAdi = ad });

    public async Task<CalisanAvansApiResponse> GetToplamAvansAsync(string dateRange)
        => await PostJsonAsync("/api/ai/toplam-avans", new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetToplamGelirAsync(string dateRange)
        => await PostJsonAsync("/api/ai/toplam-gelir", new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetToplamGiderAsync(string dateRange)
        => await PostJsonAsync("/api/ai/toplam-gider", new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetKasaBakiyeAsync(string dateRange)
        => await PostJsonAsync("/api/ai/kasa-bakiye", new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetBugunKasaDurumuAsync(string kasaIntent)
        => await PostJsonAsync("/api/ai/bugun-kasa-durumu",
            new CalisanAvansApiRequest { CalisanAdi = kasaIntent, DateRange = "Today" });

    public async Task<CalisanAvansApiResponse> GetToplamMusteriTahsilatiAsync(string dateRange)
        => await PostJsonAsync("/api/ai/toplam-musteri-tahsilati",
            new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetToplamSaticiOdemesiAsync(string dateRange)
        => await PostJsonAsync("/api/ai/toplam-satici-odemesi",
            new CalisanAvansApiRequest { DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetCalisanAvansToplamAsync(string ad, string dateRange)
        => await PostJsonAsync("/api/ai/calisan-avans-toplam",
            new CalisanAvansApiRequest { CalisanAdi = ad, DateRange = dateRange });

    public async Task<CalisanAvansApiResponse> GetMaasOdemeDagilimAsync(int? year, int? month)
    => await PostJsonAsync("/api/ai/maas-odeme-dagilim",
        new CalisanAvansApiRequest
        {
            Year = year,
            Month = month
        });

    public async Task<CalisanAvansApiResponse> GetMaasOdemeTarihleriAsync(int? year, int? month)
    => await PostJsonAsync("/api/ai/maas-odeme-tarihleri",
        new CalisanAvansApiRequest
        {
            Year = year,
            Month = month
        });
}

// =======================
// MODELLER
// =======================

public class CalisanAvansApiRequest
{
    public string CalisanAdi { get; set; } = "";
    public string DateRange { get; set; } = "ThisMonth";

    public int? Year { get; set; }
    public int? Month { get; set; }
}

public class CalisanAvansApiResponse
{
    public bool Success { get; set; }
    public string EmployeeName { get; set; } = "";
    public decimal Total { get; set; }
    public string Message { get; set; } = "";
}