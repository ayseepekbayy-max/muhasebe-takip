using FirmovaAI.Models.Ai;

namespace FirmovaAI.Services.Ai;

public class QueryInterpreter
{
    public QueryIntent Interpret(string input)
    {
        var result = new QueryIntent();

        if (string.IsNullOrWhiteSpace(input))
            return result;

        var lower = input.ToLower();

        // =========================
        // AVANS
        // =========================
        if (lower.Contains("avans") && lower.Contains("ne kadar"))
        {
            result.Intent = "CalisanAvansToplam";
            result.IsSuccess = true;

            // basit isim yakalama (örnek)
            var kelimeler = lower.Split(" ");
            result.CalisanAdi = kelimeler.FirstOrDefault(x => x.Length > 2);

            return result;
        }

        if (lower.Contains("toplam avans"))
        {
            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("son avans"))
        {
            result.Intent = "SonAvansVerilenKisi";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // KASA
        // =========================
        if (lower.Contains("bugün kasa"))
        {
            result.Intent = "BugunKasa";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("kasa giriş"))
        {
            result.Intent = "BugunKasaGiris";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("kasa çıkış"))
        {
            result.Intent = "BugunKasaCikis";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // MÜŞTERİ / CARİ
        // =========================
        if (lower.Contains("en çok borçlu"))
        {
            result.Intent = "EnBorcluMusteri";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("en alacaklı"))
        {
            result.Intent = "EnAlacakliSatici";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("müşteri tahsilat"))
        {
            result.Intent = "ToplamMusteriTahsilati";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("satıcı ödeme"))
        {
            result.Intent = "ToplamSaticiOdemesi";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // YENİ: GELİR ANALİZİ
        // =========================
        if (lower.Contains("en çok gelir") || lower.Contains("kazandıran müşteri"))
        {
            result.Intent = "EnCokGelirGetirenMusteri";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // YENİ: GİDER ANALİZİ
        // =========================
        if (lower.Contains("en çok para çıkan") || lower.Contains("en çok gider"))
        {
            result.Intent = "EnCokParaCikanCari";
            result.IsSuccess = true;
            return result;
        }

        // =========================
        // GELİR / GİDER / BAKİYE
        // =========================
        if (lower.Contains("gelir"))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("gider"))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            return result;
        }

        if (lower.Contains("bakiye"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            return result;
        }

        return result;
    }
}