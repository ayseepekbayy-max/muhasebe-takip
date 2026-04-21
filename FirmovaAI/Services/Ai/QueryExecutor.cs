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
}