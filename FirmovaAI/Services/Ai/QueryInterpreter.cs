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
    public string CalisanAdi { get; set; } = "";
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

        if (DateTime.UtcNow - Context.LastUpdated > TimeSpan.FromMinutes(30))
        {
            Context = new ConversationContext();
        }

        if (IsFollowUpQuestion(lower) && !StartsNewTopic(lower))
        {
            var followUpIntent = ResolveFollowUpIntent(lower);

            if (!string.IsNullOrWhiteSpace(followUpIntent))
            {
                result.Intent = followUpIntent;
                result.IsSuccess = true;

                if (result.Year == null)
                    result.Year = Context.Year;

                if (result.Month == null)
                    result.Month = Context.Month;

                if (string.IsNullOrWhiteSpace(result.CalisanAdi))
                    result.CalisanAdi = Context.CalisanAdi;

                UpdateContextFromIntent(followUpIntent, result.CalisanAdi);
                return result;
            }
        }

        // =========================
        // KÂR / ANALİZ
        // =========================

        if (ContainsAny(lower, "kâr", "kar") &&
            ContainsAny(lower, "ettim", "var mı", "ediyor muyum"))
        {
            result.Intent = "KarDurumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "geçen aya göre", "önceki aya göre", "geçen ayla karşılaştır"))
        {
            result.Intent = "AylikKarsilastirma";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en çok gider", "en fazla gider", "gider nereden", "gider kalemi"))
        {
            result.Intent = "EnCokGider";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en çok kazandığım müşteri", "en çok kazandıran müşteri", "en fazla kazandıran müşteri"))
        {
            result.Intent = "EnCokKazandiranMusteri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "stok durumu", "stoklar nasıl", "stoklarda durum"))
        {
            result.Intent = "StokDurumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "bu ay nasıl gidiyoruz", "bu ay nasıl", "nasıl gidiyoruz"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "şirketin durumu", "şirket durumu", "firma durumu"))
        {
            result.Intent = "GenelOzet";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // MAAŞ
        // =========================

        if (ContainsAny(lower, "maaş", "maas"))
        {
            if (ContainsAny(lower, "verdim mi", "ödedim mi", "odedim mi", "ödeme yaptım mı", "odeme yaptım mı"))
            {
                result.Intent = "MaasOdemeKontrol";
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
                return result;
            }
            var kisiAdi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisiAdi)
                && !IsTotalQuestion(lower)
                && !IsDateWord(kisiAdi))
            {
                result.CalisanAdi = kisiAdi;
                result.Intent = "CalisanMaasToplam";
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }

            if (ContainsAny(lower, "kimlere", "hangi çalışan", "hangi çalışanlara", "çalışanlara", "kim ne kadar", "kime ne kadar", "dağılım"))
            {
                result.Intent = "MaasOdemeDagilim";
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
                return result;
            }

            if (ContainsAny(lower, "hangi gün", "hangi günlerde", "ne zaman", "tarih", "tarihleri"))
            {
                result.Intent = "MaasOdemeTarihleri";
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
                return result;
            }

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
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // SAYISAL GENEL SORGULAR
        // =========================

        if (ContainsAny(lower, "kaç müşteri", "müşteri sayısı", "müşterim var", "toplam müşteri"))
        {
            result.Intent = "MusteriSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kaç çalışan", "çalışan sayısı", "personel sayısı", "kaç personel", "toplam çalışan"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kaç cari", "cari sayısı", "toplam cari"))
        {
            result.Intent = "CariSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kaç alıcı", "kaç tane alıcı", "alıcı sayısı", "toplam alıcı"))
        {
            result.Intent = "AliciSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kaç satıcı", "kaç tane satıcı", "satıcı sayısı", "toplam satıcı"))
        {
            result.Intent = "SaticiSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // STOK
        // =========================

        if (ContainsAny(lower, "stokta kaç ürün", "ürün sayısı", "stok ürün sayısı", "kaç ürün var", "toplam ürün", "stok sayısı"))
        {
            result.Intent = "StokSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower,
            "biten stok", "stokta biten", "biten ürün", "stok bitti",
            "stokta olmayan", "tükenen ürün", "hangi ürün bitmiş",
            "hangi ürünler bitmiş", "stokta kalmayan"))
        {
            result.Intent = "BitenStoklar";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en çok stok", "stokta en çok", "en fazla stok", "en fazla ürün", "en çok olan ürün"))
        {
            result.Intent = "EnCokStoktaOlanUrun";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // AVANS
        // =========================

        if (ContainsAny(lower, "avans"))
        {
            if (ContainsAny(lower, "kimlere", "hangi çalışan", "hangi çalışanlara", "hangi çalışanlarıma", "çalışanlarıma", "çalışanlara", "kim ne kadar", "kime ne kadar", "dağılım"))
            {
                result.Intent = "AvansDagilim";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month);
                return result;
            }

            if (ContainsAny(lower, "en çok kim", "en fazla kim", "en çok alan", "en fazla alan"))
            {
                result.Intent = "EnCokAvansAlan";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month);
                return result;
            }

            if (ContainsAny(lower, "son", "en son"))
            {
                result.Intent = "SonAvansVerilenKisi";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month);
                return result;
            }

            var kisiAdi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisiAdi)
                && !IsTotalQuestion(lower)
                && !IsDateWord(kisiAdi))
            {
                result.CalisanAdi = kisiAdi;
                result.Intent = "CalisanAvansToplam";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }

            result.Intent = "ToplamAvans";
            result.IsSuccess = true;
            UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month);
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
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "bugün kaç işlem", "bugün kasa işlem", "bugün kaç kasa hareketi", "bugün kaç hareket"))
        {
            result.Intent = "BugunKasaIslemSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "giriş", "gelir", "para girdi"))
        {
            result.Intent = "BugunKasaGiris";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "çıkış", "gider", "para çıktı"))
        {
            result.Intent = "BugunKasaCikis";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "bugün") && ContainsAny(lower, "kasa"))
        {
            result.Intent = "BugunKasa";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if ((ContainsAny(lower, "kasa") &&
             ContainsAny(lower, "ne kadar", "kaç", "para", "bakiye", "durum", "var mı")) ||
            ContainsAny(lower, "kasada kaç", "kasada ne kadar", "kasada para"))
        {
            result.Intent = "KasaBakiye";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "gelir", "giriş", "kazanç", "tahsilat") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "girdi")))
        {
            result.Intent = "ToplamGelir";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "gider", "çıkış", "masraf") ||
            (ContainsAny(lower, "kasa") && ContainsAny(lower, "çıktı")))
        {
            result.Intent = "ToplamGider";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // MÜŞTERİ / SATICI
        // =========================

        if (ContainsAny(lower, "müşteri tahsilatı", "müşterilerden", "müşteriden ne kadar", "toplam tahsilat"))
        {
            result.Intent = "ToplamMusteriTahsilati";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "satıcı ödemesi", "satıcılara", "satıcıya ne kadar", "toplam ödeme"))
        {
            result.Intent = "ToplamSaticiOdemesi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en borçlu", "en çok borçlu", "kim bana en çok borçlu"))
        {
            result.Intent = "EnBorcluMusteri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en alacaklı", "en çok alacaklı", "en çok ödeme yapılan"))
        {
            result.Intent = "EnAlacakliSatici";
            result.IsSuccess = true;
            UpdateContext(TopicType.Cari, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "borç", "borcu", "borçlu"))
        {
            result.CalisanAdi = ExtractPersonName(lower) ?? ExtractFirstWord(text);
            result.Intent = "MusteriBorc";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month, result.CalisanAdi);
            return result;
        }

        // =========================
        // PUANTAJ
        // =========================

        if (ContainsAny(lower, "puantaj", "geldi", "gelmedi", "izinli", "yarım gün"))
        {
            var ad = ExtractPersonName(lower) ?? ExtractFirstWord(text);

            if (!string.IsNullOrWhiteSpace(ad))
            {
                result.CalisanAdi = ad;
                result.Intent = "CalisanPuantaj";
                result.IsSuccess = true;
                UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }
        }

        result.ErrorMessage = "Soru anlaşılamadı.";
        return result;
    }

    private static string ResolveFollowUpIntent(string text)
    {
        if (Context.LastIntent == "CalisanAvansToplam" && !string.IsNullOrWhiteSpace(Context.CalisanAdi))
            return "CalisanAvansToplam";

        if (Context.LastIntent == "CalisanMaasToplam" && !string.IsNullOrWhiteSpace(Context.CalisanAdi))
            return "CalisanMaasToplam";

        switch (Context.CurrentTopic)
        {
            case TopicType.Maas:

    if (ContainsAny(text, "detay", "detay ver", "listele"))
        return "MaasOdemeDagilim";

    if (ContainsAny(text, "kime ne kadar", "kim ne kadar", "kişilere göre", "çalışanlara göre", "dağılım"))
        return "MaasOdemeDagilim";

    if (ContainsAny(text, "hangi gün", "hangi günlerde", "ne zaman", "tarih", "tarihleri"))
        return "MaasOdemeTarihleri";

    return "MaasOdemeKontrol";

            case TopicType.Avans:

            // 👇 BU SATIRI EKLE (EN ÖNEMLİ)
            if (ContainsAny(text, "detay", "detay ver", "listele"))
                return "AvansDagilim";

            if (ContainsAny(text, "en son", "son kime", "kime verdim"))
                return "SonAvansVerilenKisi";

            if (ContainsAny(text, "kimlere", "hangi çalışanlara", "çalışanlara", "kim ne kadar", "kime ne kadar", "dağılım"))
                return "AvansDagilim";

            if (ContainsAny(text, "en çok kim", "en fazla kim", "en çok alan", "en fazla alan"))
                return "EnCokAvansAlan";

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

    private static bool StartsNewTopic(string text)
    {
        return ContainsAny(text,
            "avans", "maaş", "maas", "kasa", "stok", "müşteri", "musteri",
            "cari", "alıcı", "alici", "satıcı", "satici",
            "gelir", "gider", "kâr", "kar", "borç", "borc");
    }

    private static void UpdateContextFromIntent(string intent, string? calisanAdi = null)
    {
        var year = Context.Year;
        var month = Context.Month;
        var ad = !string.IsNullOrWhiteSpace(calisanAdi) ? calisanAdi : Context.CalisanAdi;

        if (intent.StartsWith("Maas") || intent.Contains("Maas"))
            UpdateContext(TopicType.Maas, intent, year, month, ad);
        else if (intent.Contains("Avans"))
            UpdateContext(TopicType.Avans, intent, year, month, ad);
        else if (intent.Contains("Kasa") || intent.Contains("Gelir") || intent.Contains("Gider"))
            UpdateContext(TopicType.Kasa, intent, year, month, ad);
        else if (intent.Contains("Stok"))
            UpdateContext(TopicType.Stok, intent, year, month, ad);
        else if (intent.Contains("Musteri") || intent.Contains("Borclu"))
            UpdateContext(TopicType.Musteri, intent, year, month, ad);
        else if (intent.Contains("Cari") || intent.Contains("Alici") || intent.Contains("Satici"))
            UpdateContext(TopicType.Cari, intent, year, month, ad);
        else
            UpdateContext(TopicType.Genel, intent, year, month, ad);
    }

    private static void UpdateContext(TopicType topic, string intent, int? year = null, int? month = null, string? calisanAdi = null)
    {
        Context.CurrentTopic = topic;
        Context.LastIntent = intent;
        Context.LastUpdated = DateTime.UtcNow;
        Context.Year = year;
        Context.Month = month;

        if (!string.IsNullOrWhiteSpace(calisanAdi))
            Context.CalisanAdi = calisanAdi;
        else if (!intent.Contains("Calisan"))
            Context.CalisanAdi = "";
    }

    private static bool ContainsAny(string text, params string[] words)
    {
        return words.Any(w => text.Contains(w));
    }

    private static bool IsTotalQuestion(string text)
    {
        return ContainsAny(text,
            "toplam", "hepsi", "herkes", "tüm çalışan", "bütün çalışan",
            "genel toplam", "toplam kaç", "toplam ne kadar");
    }

    private static string? ExtractPersonName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text
            .Replace("’", "'")
            .Replace("`", "'");

        normalized = normalized
            .Replace("'ye", " ")
            .Replace("'ya", " ")
            .Replace("'e", " ")
            .Replace("'a", " ");

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawWord in words)
        {
            var word = rawWord
                .Trim()
                .Trim(',', '.', '?', '!', ':', ';')
                .ToLowerInvariant();

            word = RemoveTurkishSuffixes(word);

            if (word.Length < 2)
                continue;

            if (IsDateWord(word) || IsQuestionWord(word))
                continue;

            if (ContainsAny(word, "avans", "maaş", "maas"))
                continue;

            return word;
        }

        return null;
    }

    private static string RemoveTurkishSuffixes(string word)
    {
        var suffixes = new[]
        {
            "ye", "ya", "e", "a", "nin", "nın", "nun", "nün", "in", "ın", "un", "ün"
        };

        foreach (var suffix in suffixes)
        {
            if (word.Length > suffix.Length + 1 && word.EndsWith(suffix))
                return word[..^suffix.Length];
        }

        return word;
    }

    private static bool IsQuestionWord(string word)
    {
        return ContainsAny(word,
            "ayında", "ayinda", "bu", "geçen", "gecen",
            "ne", "kadar", "kaç", "tl", "para",
            "aldı", "aldi", "verdim", "verdik", "verilen",
            "ödedim", "odedim", "ödeme", "odeme", "yaptım", "yaptik", "yaptık",
            "kim", "kime", "kimlere", "hangi", "çalışan", "çalışanlara", "çalışanlarıma",
            "için", "icin", "toplam");
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

    private static bool IsDateWord(string word)
    {
        return ContainsAny(word,
            "ocak", "şubat", "subat", "mart", "nisan", "mayıs", "mayis", "haziran",
            "temmuz", "ağustos", "agustos", "eylül", "eylul", "ekim", "kasım", "kasim", "aralık", "aralik",
            "bugün", "bugun", "dün", "dun", "bu", "geçen", "gecen");
    }

    private static (int? year, int? month) ExtractMonthInfo(string text)
    {
        var months = new Dictionary<string, int>
        {
            { "ocak", 1 },
            { "şubat", 2 },
            { "subat", 2 },
            { "mart", 3 },
            { "nisan", 4 },
            { "mayıs", 5 },
            { "mayis", 5 },
            { "haziran", 6 },
            { "temmuz", 7 },
            { "ağustos", 8 },
            { "agustos", 8 },
            { "eylül", 9 },
            { "eylul", 9 },
            { "ekim", 10 },
            { "kasım", 11 },
            { "kasim", 11 },
            { "aralık", 12 },
            { "aralik", 12 }
        };

        foreach (var m in months)
        {
            if (text.Contains(m.Key))
            {
                var year = DateTime.UtcNow.Year;

                var match = System.Text.RegularExpressions.Regex.Match(text, @"(20\d{2})");
                if (match.Success)
                    year = int.Parse(match.Value);

                return (year, m.Value);
            }
        }

        return (null, null);
    }
}
