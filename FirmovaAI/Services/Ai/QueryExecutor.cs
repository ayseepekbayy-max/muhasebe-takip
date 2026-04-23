using FirmovaAI.Models.Ai;

namespace FirmovaAI.Services.Ai;

public class QueryExecutor
{
    private readonly MuhasebeApiClient _apiClient;

    public QueryExecutor(MuhasebeApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<string> ExecuteAsync(QueryIntent intent)
    {
        if (!intent.IsSuccess || string.IsNullOrWhiteSpace(intent.Intent))
            return "Sorunuzu anlayamadım.";

        switch (intent.Intent)
        {
            case "CalisanAvansToplam":
                return await GetCalisanAvansToplamAsync(intent);

            case "ToplamAvans":
                return await GetToplamAvansAsync(intent);

            case "SonAvansVerilenKisi":
                return await GetSonAvansVerilenKisiAsync();

            case "BugunKasa":
            case "BugunKasaGiris":
            case "BugunKasaCikis":
                return await GetBugunKasaAsync(intent);

            case "EnBorcluMusteri":
                return await GetEnBorcluMusteriAsync();

            case "EnAlacakliSatici":
                return await GetEnAlacakliSaticiAsync();

            case "ToplamMusteriTahsilati":
                return await GetToplamMusteriTahsilatiAsync(intent);

            case "ToplamSaticiOdemesi":
                return await GetToplamSaticiOdemesiAsync(intent);

            default:
                return "Bu sorgu tipi henüz desteklenmiyor.";
        }
    }

    private async Task<string> GetCalisanAvansToplamAsync(QueryIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.CalisanAdi))
            return "Çalışan adı anlaşılamadı.";

        var result = await _apiClient.GetCalisanAvansToplamAsync(
            intent.CalisanAdi,
            intent.DateRange ?? "ThisMonth");

        return result.Message;
    }

    private async Task<string> GetToplamAvansAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetToplamAvansAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }

    private async Task<string> GetSonAvansVerilenKisiAsync()
    {
        var result = await _apiClient.GetSonAvansVerilenKisiAsync();
        return result.Message;
    }

    private async Task<string> GetBugunKasaAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetBugunKasaDurumuAsync(intent.Intent ?? "BugunKasa");
        return result.Message;
    }

    private async Task<string> GetEnBorcluMusteriAsync()
    {
        var result = await _apiClient.GetEnBorcluMusteriAsync();
        return result.Message;
    }

    private async Task<string> GetEnAlacakliSaticiAsync()
    {
        var result = await _apiClient.GetEnAlacakliSaticiAsync();
        return result.Message;
    }

    private async Task<string> GetToplamMusteriTahsilatiAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetToplamMusteriTahsilatiAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }

    private async Task<string> GetToplamSaticiOdemesiAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetToplamSaticiOdemesiAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }
}