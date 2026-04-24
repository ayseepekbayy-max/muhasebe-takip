using System.Net.Http.Json;

namespace FirmovaAI.Services.Ai;

public class MuhasebeApiClient
{
    private readonly HttpClient _httpClient;

    public MuhasebeApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CalisanAvansApiResponse> GetCalisanAvansToplamAsync(string calisanAdi, string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            CalisanAdi = calisanAdi,
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/calisan-avans-toplam", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetToplamAvansAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-avans", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetSonAvansVerilenKisiAsync()
    {
        var response = await _httpClient.PostAsync("/api/ai/son-avans-verilen-kisi", null);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetBugunKasaDurumuAsync(string kasaIntent)
    {
        var request = new CalisanAvansApiRequest
        {
            CalisanAdi = kasaIntent,
            DateRange = "Today"
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/bugun-kasa-durumu", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetEnBorcluMusteriAsync()
    {
        var response = await _httpClient.PostAsync("/api/ai/en-borclu-musteri", null);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetEnAlacakliSaticiAsync()
    {
        var response = await _httpClient.PostAsync("/api/ai/en-alacakli-satici", null);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetToplamMusteriTahsilatiAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-musteri-tahsilati", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetToplamSaticiOdemesiAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-satici-odemesi", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetToplamGelirAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-gelir", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetToplamGiderAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-gider", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetKasaBakiyeAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/kasa-bakiye", request);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetSonKasaHareketleriAsync()
    {
        var response = await _httpClient.PostAsync("/api/ai/son-kasa-hareketleri", null);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync(response);
    }

    public async Task<CalisanAvansApiResponse> GetMusteriBorcAsync(string ad)
    {
        var request = new CalisanAvansApiRequest
        {
            CalisanAdi = ad
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/musteri-borc", request);
        response.EnsureSuccessStatusCode();

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
}

public class CalisanAvansApiRequest
{
    public string CalisanAdi { get; set; } = "";
    public string DateRange { get; set; } = "ThisMonth";
}

public class CalisanAvansApiResponse
{
    public bool Success { get; set; }
    public string EmployeeName { get; set; } = "";
    public decimal Total { get; set; }
    public string Message { get; set; } = "";
}