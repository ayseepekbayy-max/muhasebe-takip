using FirmovaAI.Models.Ai;

namespace FirmovaAI.Services.Ai;

public enum TopicType
{
    None,
    Maas,
    Avans,
    Kasa,
    Stok,
    Musteri,
    Cari,
    Genel
}

public class ConversationContext
{
    public TopicType CurrentTopic { get; set; } = TopicType.None;
    public string LastIntent { get; set; } = "";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public int? Year { get; set; }
    public int? Month { get; set; }
}

public class QueryInterpreter
{
    private static ConversationContext Context { get; set; } = new();

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

        var (year, month) = ExtractMonthInfo(lower);
        result.Year = year;
        result.Month = month;

        // Çok eski konu kalmasın diye 30 dakika sonra bağlam sıfırlanır
        if (DateTime.UtcNow - Context.LastUpdated > TimeSpan.FromMinutes(30))
        {
            Context = new ConversationContext();
        }

        // =========================
        // BAĞLAMA GÖRE DEVAM SORULARI
        // =========================

        if (IsFollowUpQuestion(lower))
        {
            var followUpIntent = ResolveFollowUpIntent(lower);

            if (!string.IsNullOrWhiteSpace(followUpIntent))
            {
                result.Intent = followUpIntent;
                result.IsSuccess = true;

                result.Year = Context.Year;
                result.Month = Context.Month;

                UpdateContextFromIntent(followUpIntent);
                return result;
            }
        }

        // =========================
        // YENİ AKILLI SORULAR
        // =========================

        if (ContainsAny(lower, "kâr", "kar") &&
            ContainsAny(lower, "ettim", "var mı", "ediyor muyum"))
        {
            result.Intent = "KarDurumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "geçen aya göre", "önceki aya göre", "geçen ayla karşılaştır"))
        {
            result.Intent = "AylikKarsilastirma";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "en çok gider", "en fazla gider", "gider nereden", "gider kalemi"))
        {
            result.Intent = "EnCokGider";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "en çok kazandığım müşteri", "en çok kazandıran müşteri", "en fazla kazandıran müşteri"))
        {
            result.Intent = "EnCokKazandiranMusteri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "stok durumu", "stoklar nasıl", "stoklarda durum"))
        {
            result.Intent = "StokDurumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "bu ay nasıl gidiyoruz", "bu ay nasıl", "nasıl gidiyoruz"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "şirketin durumu", "şirket durumu", "firma durumu"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "maaş") &&
        ContainsAny(lower, "ödedim", "ödeme yaptım", "ödemesi yaptım", "maaş ödemesi"))
        {
            result.Intent = "MaasOdemeKontrol";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // GENEL ÖZET / DURUM
        // =========================

        if (ContainsAny(lower,
        "genel durum", "durum nasıl", "genel özet", "özet ver",
        "firma durumu", "işletme durumu", "şirket durumu",
        "işler nasıl gidiyor", "işler iyi mi gidiyor",
        "şirket nasıl gidiyor", "firma nasıl",
        "durumumuz nasıl", "işler ne durumda",
        "kasa iyi mi kötü mü", "param artıyor mu azalıyor mu",
        "durum kötü mü", "işler iyi mi"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        // =========================
        // SAYISAL GENEL SORGULAR
        // =========================

        if (ContainsAny(lower, "kaç müşteri", "müşteri sayısı", "müşterim var", "toplam müşteri"))
        {
            result.Intent = "MusteriSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "kaç çalışan", "çalışan sayısı", "personel sayısı", "kaç personel", "toplam çalışan"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "kaç cari", "cari sayısı", "toplam cari"))
        {
            result.Intent = "CariSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "kaç alıcı", "kaç tane alıcı", "alıcı sayısı", "toplam alıcı"))
        {
            result.Intent = "AliciSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "kaç satıcı", "kaç tane satıcı", "satıcı sayısı", "toplam satıcı"))
        {
            result.Intent = "SaticiSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent);
            return result;
        }

        // =========================
        // STOK
        // =========================

        if (ContainsAny(lower, "stokta kaç ürün", "ürün sayısı", "stok ürün sayısı", "kaç ürün var", "toplam ürün", "stok sayısı"))
        {
            result.Intent = "StokSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent);
            return result;
        }

        if (ContainsAny(lower,
            "biten stok", "stokta biten", "biten ürün", "stok bitti",
            "stokta olmayan", "tükenen ürün", "hangi ürün bitmiş",
            "hangi ürünler bitmiş", "stokta kalmayan"))
        {
            result.Intent = "BitenStoklar";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "en çok stok", "stokta en çok", "en fazla stok", "en fazla ürün", "en çok olan ürün"))
        {
            result.Intent = "EnCokStoktaOlanUrun";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent);
            return result;
        }

        // =========================
        // AVANS
        // =========================

        if (ContainsAny(lower, "son", "en son") && ContainsAny(lower, "avans"))
        {
            result.Intent = "SonAvansVerilenKisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Avans, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "avans"))
        {
            var ad = ExtractFirstWord(text);

            if (!string.IsNullOrWhiteSpace(ad) &&
                !ContainsAny(lower, "toplam avans", "toplam", "herkes", "tüm çalışan", "bütün çalışan"))
            {
                result.CalisanAdi = ad;
                result.Intent = "CalisanAvansToplam";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent);
                return result;
            }

            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            UpdateContext(TopicType.Avans, result.Intent);
            return result;
        }

        // =========================
        // KASA
        // =========================

        if (ContainsAny(lower, "son", "son 10", "son işlemler") &&
            ContainsAny(lower, "kasa", "hareket"))
        {
            result.Intent = "SonKasaHareketleri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "bugün kaç işlem", "bugün kasa işlem", "bugün kaç kasa hareketi", "bugün kaç hareket"))
        {
            result.Intent = "BugunKasaIslemSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "giriş", "gelir", "para girdi"))
        {
            result.Intent = "BugunKasaGiris";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "çıkış", "gider", "para çıktı"))
        {
            result.Intent = "BugunKasaCikis";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "kasa"))
        {
            result.Intent = "BugunKasa";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if ((ContainsAny(lower, "kasa") &&
             ContainsAny(lower, "ne kadar", "kaç", "para", "bakiye", "durum", "var mı")) ||
            ContainsAny(lower, "kasada kaç", "kasada ne kadar", "kasada para"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        // =========================
        // GELİR / GİDER
        // =========================

        if (ContainsAny(lower, "gelir", "giriş", "kazanç", "tahsilat") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "girdi")))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "gider", "çıkış", "masraf") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "çıktı")))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent);
            return result;
        }

        // =========================
        // MÜŞTERİ / SATICI
        // =========================

        if (ContainsAny(lower, "müşteri tahsilatı", "müşterilerden", "müşteriden ne kadar", "toplam tahsilat"))
        {
            result.Intent = "ToplamMusteriTahsilati";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "satıcı ödemesi", "satıcılara", "satıcıya ne kadar", "toplam ödeme"))
        {
            result.Intent = "ToplamSaticiOdemesi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "en borçlu", "en çok borçlu", "kim bana en çok borçlu"))
        {
            result.Intent = "EnBorcluMusteri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "en alacaklı", "en çok alacaklı", "en çok ödeme yapılan"))
        {
            result.Intent = "EnAlacakliSatici";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent);
            return result;
        }

        if (ContainsAny(lower, "borç", "borcu", "borçlu"))
        {
            result.CalisanAdi = ExtractFirstWord(text);
            result.Intent = "MusteriBorc";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent);
            return result;
        }

        // =========================
        // PUANTAJ
        // =========================

        if (ContainsAny(lower, "puantaj", "geldi", "gelmedi", "izinli", "yarım gün"))
        {
            var ad = ExtractFirstWord(text);

            if (!string.IsNullOrWhiteSpace(ad))
            {
                result.CalisanAdi = ad;
                result.Intent = "CalisanPuantaj";
                result.IsSuccess = true;
                UpdateContext(TopicType.Genel, result.Intent);
                return result;
            }
        }

        result.ErrorMessage = "Soru anlaşılamadı.";
        return result;
    }

    private static string ResolveFollowUpIntent(string text)
    {
        switch (Context.CurrentTopic)
        {
            case TopicType.Maas:
                if (ContainsAny(text, "kime ne kadar", "kim ne kadar", "kişilere göre", "çalışanlara göre", "dağılım"))
                    return "MaasOdemeDagilim";

                if (ContainsAny(text, "hangi gün", "hangi günlerde", "ne zaman", "tarih", "tarihleri"))
                    return "MaasOdemeTarihleri";

                return "MaasOdemeKontrol";

            case TopicType.Avans:
                if (ContainsAny(text, "en son", "son kime", "kime verdim"))
                    return "SonAvansVerilenKisi";

                return "ToplamAvans";

            case TopicType.Kasa:
                if (ContainsAny(text, "detay", "son işlemler", "hareketler", "listele"))
                    return "SonKasaHareketleri";

                if (ContainsAny(text, "giriş", "gelir", "para girdi"))
                    return "ToplamGelir";

                if (ContainsAny(text, "çıkış", "gider", "masraf", "para çıktı"))
                    return "ToplamGider";

                return "KasaBakiye";

            case TopicType.Stok:
                if (ContainsAny(text, "biten", "tükenen", "kalmayan"))
                    return "BitenStoklar";

                if (ContainsAny(text, "en çok", "en fazla"))
                    return "EnCokStoktaOlanUrun";

                return "StokDurumu";

            case TopicType.Musteri:
                if (ContainsAny(text, "en borçlu", "kim borçlu", "borçlu"))
                    return "EnBorcluMusteri";

                if (ContainsAny(text, "en çok kazandıran", "en çok kazandığım"))
                    return "EnCokKazandiranMusteri";

                return "MusteriSayisi";

            case TopicType.Cari:
                if (ContainsAny(text, "alıcı"))
                    return "AliciSayisi";

                if (ContainsAny(text, "satıcı"))
                    return "SaticiSayisi";

                return "CariSayisi";

            case TopicType.Genel:
                if (ContainsAny(text, "geçen aya göre", "karşılaştır"))
                    return "AylikKarsilastirma";

                if (ContainsAny(text, "kâr", "kar"))
                    return "KarDurumu";

                return "GenelOzet";

            default:
                return "";
        }
    }

    private static bool IsFollowUpQuestion(string text)
    {
        if (Context.CurrentTopic == TopicType.None)
            return false;

        return ContainsAny(text,
            "detay", "detay ver", "devam", "listele",
            "kim", "kime", "ne zaman", "hangi gün", "hangi günlerde",
            "ne kadar", "kaç", "tarih", "tarihleri",
            "en çok", "en fazla", "biten", "tükenen",
            "giriş", "çıkış", "gelir", "gider");
    }

    private static void UpdateContextFromIntent(string intent)
    {
        if (intent.StartsWith("Maas"))
            UpdateContext(TopicType.Maas, intent);
        else if (intent.Contains("Avans"))
            UpdateContext(TopicType.Avans, intent);
        else if (intent.Contains("Kasa") || intent.Contains("Gelir") || intent.Contains("Gider"))
            UpdateContext(TopicType.Kasa, intent);
        else if (intent.Contains("Stok"))
            UpdateContext(TopicType.Stok, intent);
        else if (intent.Contains("Musteri") || intent.Contains("Borclu"))
            UpdateContext(TopicType.Musteri, intent);
        else if (intent.Contains("Cari") || intent.Contains("Alici") || intent.Contains("Satici"))
            UpdateContext(TopicType.Cari, intent);
        else
            UpdateContext(TopicType.Genel, intent);
    }

    private static void UpdateContext(TopicType topic, string intent, int? year = null, int? month = null)
{
    Context.CurrentTopic = topic;
    Context.LastIntent = intent;
    Context.LastUpdated = DateTime.UtcNow;

    Context.Year = year;
    Context.Month = month;
}

    private static bool ContainsAny(string text, params string[] words)
    {
        return words.Any(w => text.Contains(w));
    }

    private static string DetectDateRange(string text)
{
    if (ContainsAny(text, "bugün", "bugünkü"))
        return "Today";

    if (ContainsAny(text, "dün", "dünkü"))
        return "Yesterday";

    if (ContainsAny(text, "geçen ay", "önceki ay", "bir önceki ay"))
        return "LastMonth";

    if (ContainsAny(text, "bu ay", "bu ayki", "içinde bulunduğumuz ay"))
        return "ThisMonth";

    if (ContainsAny(text, "tüm zamanlar", "hepsi", "tamamı", "toplam genel", "genel toplam", "başından beri"))
        return "All";

    return "ThisMonth";
}

    private static string DetectRequestType(string text)
    {
        if (ContainsAny(text, "ne kadar", "toplam", "kaç", "kaç tl", "kaç para"))
            return "Total";

        if (ContainsAny(text, "detay", "ayrıntı", "liste", "listele"))
            return "Detail";

        return "List";
    }

    private static string? ExtractFirstWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length > 0 ? words[0].ToLowerInvariant() : null;
    }

    private static (int? year, int? month) ExtractMonthInfo(string text)
{
    var months = new Dictionary<string, int>
    {
        { "ocak", 1 },
        { "şubat", 2 },
        { "mart", 3 },
        { "nisan", 4 },
        { "mayıs", 5 },
        { "haziran", 6 },
        { "temmuz", 7 },
        { "ağustos", 8 },
        { "eylül", 9 },
        { "ekim", 10 },
        { "kasım", 11 },
        { "aralık", 12 }
    };

    foreach (var m in months)
    {
        if (text.Contains(m.Key))
        {
            var year = DateTime.UtcNow.Year;

            // "2024 nisan" gibi yıl varsa yakala
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(20\d{2})");
            if (match.Success)
                year = int.Parse(match.Value);

            return (year, m.Value);
        }
    }

    return (null, null);
}
}