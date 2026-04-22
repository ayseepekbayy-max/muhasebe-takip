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

        var result = await response.Content.ReadFromJsonAsync<CalisanAvansApiResponse>();
        return result ?? new CalisanAvansApiResponse
        {
            Success = false,
            Message = "API cevabı okunamadı."
        };
    }

    public async Task<CalisanAvansApiResponse> GetToplamAvansAsync(string dateRange)
    {
        var request = new CalisanAvansApiRequest
        {
            CalisanAdi = "",
            DateRange = dateRange
        };

        var response = await _httpClient.PostAsJsonAsync("/api/ai/toplam-avans", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CalisanAvansApiResponse>();
        return result ?? new CalisanAvansApiResponse
        {
            Success = false,
            Message = "API cevabı okunamadı."
        };
    }

    public async Task<CalisanAvansApiResponse> GetSonAvansVerilenKisiAsync()
    {
        var response = await _httpClient.PostAsync("/api/ai/son-avans-verilen-kisi", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CalisanAvansApiResponse>();
        return result ?? new CalisanAvansApiResponse
        {
            Success = false,
            Message = "API cevabı okunamadı."
        };
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