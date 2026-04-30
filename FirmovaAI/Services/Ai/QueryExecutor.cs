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

            case "ToplamGelir":
                return await GetToplamGelirAsync(intent);

            case "ToplamGider":
                return await GetToplamGiderAsync(intent);

            case "KasaBakiye":
                return await GetKasaBakiyeAsync(intent);

            case "SonKasaHareketleri":
                return await GetSonKasaHareketleriAsync();

            case "MusteriBorc":
                return await GetMusteriBorcAsync(intent);

            case "MusteriSayisi":
                return (await _apiClient.GetMusteriSayisiAsync()).Message;

            case "CalisanSayisi":
                return (await _apiClient.GetCalisanSayisiAsync()).Message;

            case "CariSayisi":
                return (await _apiClient.GetCariSayisiAsync()).Message;

            case "AliciSayisi":
                return (await _apiClient.GetAliciSayisiAsync()).Message;

            case "SaticiSayisi":
                return (await _apiClient.GetSaticiSayisiAsync()).Message;

            case "StokSayisi":
                return (await _apiClient.GetStokSayisiAsync()).Message;

            case "BitenStoklar":
                return (await _apiClient.GetBitenStoklarAsync()).Message;

            case "EnCokStoktaOlanUrun":
                return (await _apiClient.GetEnCokStoktaOlanUrunAsync()).Message;

            case "BugunKasaIslemSayisi":
                return (await _apiClient.GetBugunKasaIslemSayisiAsync()).Message;

            case "GenelOzet":
                return (await _apiClient.GetGenelOzetAsync()).Message;

            case "KarDurumu":
                return (await _apiClient.GetKarDurumuAsync()).Message;

            case "AylikKarsilastirma":
                return (await _apiClient.GetAylikKarsilastirmaAsync()).Message;

            case "EnCokGider":
                return (await _apiClient.GetEnCokGiderAsync()).Message;

            case "EnCokKazandiranMusteri":
                return (await _apiClient.GetEnCokKazandiranMusteriAsync()).Message;

            case "StokDurumu":
                return (await _apiClient.GetStokDurumuAsync()).Message;

            case "MaasOdemeKontrol":
                return (await _apiClient.GetMaasOdemeKontrolAsync()).Message;

            case "CalisanPuantaj":
                return await GetCalisanPuantajAsync(intent);

            case "MaasOdemeDagilim":
            return (await _apiClient.GetMaasOdemeDagilimAsync()).Message;

            case "MaasOdemeTarihleri":
            return (await _apiClient.GetMaasOdemeTarihleriAsync()).Message;
            default:
                return "Bu sorgu tipi henüz desteklenmiyor.";
        }
    }

    private async Task<string> GetCalisanPuantajAsync(QueryIntent intent)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(intent.CalisanAdi))
            return "Çalışan adı anlaşılamadı.";

        return "Çalışan puantaj sorgusu için API tarafındaki bağlantı henüz tamamlanmadı. Önce derleme hatasını kaldırdım; sonraki adımda MuhasebeApiClient içine puantaj metodunu eklememiz gerekiyor.";
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

    private async Task<string> GetToplamGelirAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetToplamGelirAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }

    private async Task<string> GetToplamGiderAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetToplamGiderAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }

    private async Task<string> GetKasaBakiyeAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetKasaBakiyeAsync(intent.DateRange ?? "ThisMonth");
        return result.Message;
    }

    private async Task<string> GetSonKasaHareketleriAsync()
    {
        var result = await _apiClient.GetSonKasaHareketleriAsync();
        return result.Message;
    }

    private async Task<string> GetMusteriBorcAsync(QueryIntent intent)
    {
        var result = await _apiClient.GetMusteriBorcAsync(intent.CalisanAdi ?? "");
        return result.Message;
    }
}