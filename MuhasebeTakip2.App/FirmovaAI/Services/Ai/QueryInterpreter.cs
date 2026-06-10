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

        // EN ÜST ÖNCELİK: AVANS
        // Genel "en fazla avans" soruları önce yakalanır.
        // Çalışan adı/soyadı geçen avans soruları ise kesin olarak çalışan bazlı avansa gider.
        if (ContainsAny(lower, "avans"))
        {
            if (ContainsAny(lower,
                "en çok avans", "en cok avans",
                "en fazla avans",
                "en çok avans alan", "en cok avans alan",
                "en fazla avans alan",
                "en çok kim avans", "en cok kim avans",
                "en fazla kim avans",
                "en çok avansı kim", "en cok avansi kim",
                "en fazla avansı kim", "en fazla avansi kim"))
            {
                result.Intent = "EnCokAvansAlan";
                result.IsSuccess = true;
                UpdateContext(TopicType.Avans, result.Intent, result.Year, result.Month);
                return result;
            }

            var erkenKisiAdi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(erkenKisiAdi))
            {
                result.CalisanAdi = erkenKisiAdi;
                result.Intent = "CalisanAvansToplam";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month,
                    result.CalisanAdi);

                return result;
            }
        }

        if (ContainsAny(lower, "devamsızlık", "devamsizlik", "puantaj", "gelmedi", "izinli"))
        {
            var kisi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisi))
            {
                result.Intent = "CalisanDevamsizlik";
                result.CalisanAdi = kisi;
                result.IsSuccess = true;
                UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }

            result.Intent = "EnCokDevamsizlikYapan";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
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

        if (ContainsAny(lower,
            "işletme analizi", "isletme analizi",
            "şirket performansı", "sirket performansi",
            "şirket performansını göster", "sirket performansini goster",
            "finansal yorum", "risk var mı", "risk var mi",
            "nakit akış", "nakit akis",
            "harcamalar normal mi",
            "şirket büyüyor mu", "sirket buyuyor mu"))
        {
            result.Intent = "AkilliIsletmeYorumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower,
            "müşteri durumunu göster", "musteri durumunu goster",
            "müşteri durumu", "musteri durumu",
            "müşterilerin listesini göster", "musterilerin listesini goster",
            "müşteri listesi", "musteri listesi"))
        {
            result.Intent = "MusteriSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower,
            "stok hareketlerini göster", "stok hareketlerini goster",
            "stok hareketleri",
            "en fazla satılan ürün", "en fazla satilan urun",
            "en çok satılan ürün", "en cok satilan urun"))
        {
            result.Intent = "StokDurumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Stok, result.Intent, result.Year, result.Month);
            return result;
        }

        // =========================
        // KÂR / ANALİZ
        // =========================

        if (ContainsAny(lower, "akıllı işletme yorumu", "akilli isletme yorumu", "işletme yorumu", "isletme yorumu", "bu ay işletme durumum nasıl", "bu ay isletme durumum nasil", "işletme durumum nasıl", "isletme durumum nasil"))
        {
            result.Intent = "AkilliIsletmeYorumu";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kasam arttı mı", "kasam artti mi", "kasa arttı mı", "kasa artti mi", "kasam azaldı mı", "kasam azaldi mi", "kasa azaldı mı", "kasa azaldi mi", "kasa artış", "kasa artis", "kasa azalış", "kasa azalis"))
        {
            result.Intent = "KasaArtisAzalis";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "son 7 gün kasa", "son yedi gün kasa", "son 7 gun kasa", "son yedi gun kasa", "haftalık kasa", "haftalik kasa"))
        {
            result.Intent = "Son7GunKasaOzeti";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "günlük ortalama gider", "gunluk ortalama gider", "günlük gider ortalaması", "gunluk gider ortalamasi", "ortalama gider"))
        {
            result.Intent = "GunlukOrtalamaGider";
            result.IsSuccess = true;
            UpdateContext(TopicType.Kasa, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "en fazla devamsızlık", "en fazla devamsizlik", "en çok devamsızlık", "en cok devamsizlik", "devamsızlık yapan", "devamsizlik yapan"))
        {
            result.Intent = "EnCokDevamsizlikYapan";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "devamsızlığı kaç", "devamsizligi kac", "devamsızlık kaç", "devamsizlik kac", "kaç gün gelmedi", "kac gun gelmedi", "puantaj özeti", "puantaj ozeti"))
        {
            var kisi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisi))
            {
                result.Intent = "CalisanDevamsizlik";
                result.CalisanAdi = kisi;
                result.IsSuccess = true;
                UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }
        }

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

        if (ContainsAny(lower,
            "en yüksek maaş",
            "en yuksek maas",
            "en yüksek maaşı",
            "en yuksek maasi",
            "en fazla maaş",
            "en fazla maas",
            "en yüksek maaşı kim aldı",
            "en yuksek maasi kim aldi",
            "en fazla maaşı kim aldı",
            "en fazla maasi kim aldi"))
        {
            result.Intent = "EnYuksekMaas";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "personel gideri", "personel masrafı", "personel masrafi", "personel maliyeti", "personel maliyet", "toplam personel maliyeti", "toplam personel maliyet", "çalışan gideri", "calisan gideri", "çalışan masrafı", "calisan masrafi", "çalışan maliyeti", "calisan maliyeti", "çalışan maliyet", "calisan maliyet", "toplam personel gideri"))
        {
            result.Intent = "PersonelGideri";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "ortalama maaş", "ortalama maas", "maaş ortalaması", "maas ortalamasi", "maaş ortalamasi"))
        {
            result.Intent = "OrtalamaMaas";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "son maaş ödemesi", "son maas odemesi", "en son maaş", "en son maas", "son maaş tarihi", "son maas tarihi"))
        {
            result.Intent = "SonMaasOdemesi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "maaşı kapanmayan", "maasi kapanmayan", "maaşı kapanmadı", "maasi kapanmadi", "maaşı henüz kapanmayan", "maasi henuz kapanmayan", "maaşı arşivlenmeyen", "maasi arsivlenmeyen"))
        {
            result.Intent = "MaasiKapanmayanCalisanlar";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "maaşa göre avans", "maasa gore avans", "avans oranı", "avans orani", "maaş avans oranı", "maas avans orani"))
        {
            result.Intent = "MaasAvansOrani";
            result.IsSuccess = true;
            UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kalan maaş", "kalan maas", "maaştan kalan", "maastan kalan", "ne kadar maaşı kaldı", "ne kadar maasi kaldi", "maaşı kaldı", "maasi kaldi"))
        {
            var kisi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisi))
            {
                result.Intent = "CalisanKalanMaas";
                result.CalisanAdi = kisi;
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }
        }

        if (ContainsAny(lower, "maaş özeti", "maas ozeti", "maaş detay", "maas detay", "maaş bilgisi", "maas bilgisi"))
        {
            var kisi = ExtractPersonName(lower);

            if (!string.IsNullOrWhiteSpace(kisi))
            {
                result.Intent = "CalisanMaasOzet";
                result.CalisanAdi = kisi;
                result.IsSuccess = true;
                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month, result.CalisanAdi);
                return result;
            }
        }

        if (ContainsAny(lower, "maaş") && ContainsAny(lower, "ne kadar"))
        {
            var kisi = ExtractPersonName(lower);

            if (!string.IsNullOrEmpty(kisi))
            {
                result.Intent = "CalisanMaasToplam";
                result.CalisanAdi = kisi;
                result.IsSuccess = true;

                UpdateContext(TopicType.Maas, result.Intent, result.Year, result.Month);
                return result;
            }
        }

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
// ÇALIŞAN SAYISI
// =========================

        if (ContainsAny(lower,
            "kaç çalışan",
            "çalışan sayısı",
            "personel sayısı",
            "kaç personel",
            "toplam çalışan",
            "çalışanım var",
            "personelim var",
            "personel sayım",
            "çalışan sayım"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;

            UpdateContext(
                TopicType.Genel,
                result.Intent,
                result.Year,
                result.Month);

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

        // Çalışan sayısı, müşteri sayısından önce kontrol edilmeli.
        // Çünkü "kaç çalışanım var" gibi sorular bazen genel sayısal sorgularla karışabiliyor.
        if (ContainsAny(lower,
            "kaç çalışan",
            "çalışan sayısı",
            "personel sayısı",
            "kaç personel",
            "toplam çalışan",
            "çalışanım var",
            "personelim var"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower, "kaç müşteri", "müşteri sayısı", "müşterim var", "toplam müşteri"))
        {
            result.Intent = "MusteriSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Musteri, result.Intent, result.Year, result.Month);
            return result;
        }

        if (ContainsAny(lower,
            "çalışanları listele",
            "calisanlari listele",
            "çalışan listesi",
            "calisan listesi",
            "personel listesi",
            "personelleri göster",
            "personelleri goster",
            "çalışanları göster",
            "calisanlari goster"))
        {
            result.Intent = "CalisanListesi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }
        if (ContainsAny(lower, "kaç çalışan", "çalışan sayısı", "personel sayısı", "kaç personel", "toplam çalışan"))
        {
            result.Intent = "CalisanSayisi";
            result.IsSuccess = true;
            UpdateContext(TopicType.Genel, result.Intent, result.Year, result.Month);
            return result;
        }
        if (ContainsAny(lower,
        "çalışanları listele",
        "calisanlari listele",
        "personel listesi",
        "çalışan listesi",
        "calisan listesi",
        "personelleri göster",
        "personelleri goster"))
    {
        result.Intent = "CalisanListesi";
        result.IsSuccess = true;

        UpdateContext(
            TopicType.Genel,
            result.Intent,
            result.Year,
            result.Month);

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
            var kisiAdi = ExtractPersonName(lower);

            // ÇALIŞAN BAZLI AVANS
            if (!string.IsNullOrWhiteSpace(kisiAdi)
                && !IsDateWord(kisiAdi))
            {
                result.CalisanAdi = kisiAdi;
                result.Intent = "CalisanAvansToplam";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month,
                    result.CalisanAdi);

                return result;
            }

            // GENEL AVANS
            if (ContainsAny(lower,
                "avans verdim mi",
                "avans verdik mi",
                "bu ay avans",
                "toplam avans",
                "avans var mı",
                "avans var mi",
                "avans kaydı var mı",
                "avans kaydi var mi"))
            {
                result.CalisanAdi = "";
                result.Intent = "ToplamAvans";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month);

                return result;
            }

            // AVANS DAĞILIMI
            if (ContainsAny(lower,
                "kimlere",
                "hangi çalışan",
                "hangi çalışanlara",
                "hangi çalışanlarıma",
                "çalışanlarıma",
                "çalışanlara",
                "kim ne kadar",
                "kime ne kadar",
                "dağılım"))
            {
                result.Intent = "AvansDagilim";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month);

                return result;
            }

            // EN ÇOK AVANS ALAN
            if (ContainsAny(lower,
                "en çok kim",
                "en cok kim",
                "en fazla kim",
                "en çok alan",
                "en cok alan",
                "en fazla alan",
                "en çok avans",
                "en cok avans",
                "en fazla avans"))
            {
                result.Intent = "EnCokAvansAlan";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month);

                return result;
            }

            // SON AVANS
            if (ContainsAny(lower, "son", "en son"))
            {
                result.Intent = "SonAvansVerilenKisi";
                result.IsSuccess = true;

                UpdateContext(
                    TopicType.Avans,
                    result.Intent,
                    result.Year,
                    result.Month);

                return result;
            }

            result.Intent = "ToplamAvans";
            result.IsSuccess = true;

            UpdateContext(
                TopicType.Avans,
                result.Intent,
                result.Year,
                result.Month);

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

    if (ContainsAny(text, "maaşı kapanmayan", "maasi kapanmayan", "kapanmayan"))
        return "MaasiKapanmayanCalisanlar";

    if (ContainsAny(text, "oran", "avans oranı", "avans orani"))
        return "MaasAvansOrani";

    if (ContainsAny(text, "ortalama", "maaş ortalaması", "maas ortalamasi"))
        return "OrtalamaMaas";

    if (ContainsAny(text, "son maaş", "son maas", "son ödeme", "son odeme"))
        return "SonMaasOdemesi";

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
                if (ContainsAny(text, "son 7", "son yedi", "haftalık", "haftalik"))
                    return "Son7GunKasaOzeti";

                if (ContainsAny(text, "ortalama", "günlük", "gunluk"))
                    return "GunlukOrtalamaGider";

                if (ContainsAny(text, "arttı", "artti", "azaldı", "azaldi", "artış", "artis", "azalış", "azalis"))
                    return "KasaArtisAzalis";

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
                if (ContainsAny(text, "yorum", "analiz", "akıllı", "akilli"))
                    return "AkilliIsletmeYorumu";

                if (ContainsAny(text, "devamsızlık", "devamsizlik", "gelmedi"))
                    return "EnCokDevamsizlikYapan";

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
    "çalışan", "calisan", "personel",
    "cari", "alıcı", "alici", "satıcı", "satici",
    "gelir", "gider", "kâr", "kar", "borç", "borc",
    "devamsızlık", "devamsizlik", "puantaj", "gelmedi", "izinli");
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

        var normalized = NormalizeNameText(text);

        var calisanEslesmeleri = new Dictionary<string, string>
        {
            { "ali samet bulut", "Ali Samet Bulut" },
            { "ali samet", "Ali Samet Bulut" },
            { "samet bulut", "Ali Samet Bulut" },
            { "ali", "Ali Samet Bulut" },
            { "samet", "Ali Samet Bulut" },
            { "bulut", "Ali Samet Bulut" },

            { "ayşe nur pekbay", "Ayşe Nur Pekbay" },
            { "ayse nur pekbay", "Ayşe Nur Pekbay" },
            { "ayşe nur", "Ayşe Nur Pekbay" },
            { "ayse nur", "Ayşe Nur Pekbay" },
            { "ayşenur", "Ayşe Nur Pekbay" },
            { "aysenur", "Ayşe Nur Pekbay" },
            { "ayşe", "Ayşe Nur Pekbay" },
            { "ayse", "Ayşe Nur Pekbay" },
            { "nur pekbay", "Ayşe Nur Pekbay" },
            { "pekbay", "Ayşe Nur Pekbay" },

            { "ozan kılıç", "Ozan Kılıç" },
            { "ozan kilic", "Ozan Kılıç" },
            { "ozan", "Ozan Kılıç" },
            { "kılıç", "Ozan Kılıç" },
            { "kilic", "Ozan Kılıç" },

            { "nurettin el müslüm", "Nurettin El Müslüm" },
            { "nurettin el muslim", "Nurettin El Müslüm" },
            { "nurettin el", "Nurettin El Müslüm" },
            { "el müslüm", "Nurettin El Müslüm" },
            { "el muslim", "Nurettin El Müslüm" },
            { "nurettin", "Nurettin El Müslüm" },
            { "müslüm", "Nurettin El Müslüm" },
            { "muslim", "Nurettin El Müslüm" }
        };

        foreach (var item in calisanEslesmeleri.OrderByDescending(x => x.Key.Length))
        {
            if (ContainsWholePhrase(normalized, NormalizeNameText(item.Key)))
                return item.Value;
        }

        return null;
    }

    private static string NormalizeNameText(string text)
    {
        var value = (text ?? "")
            .ToLowerInvariant()
            .Replace("’", "'")
            .Replace("`", "'");

        value = value
            .Replace("'ye", " ")
            .Replace("'ya", " ")
            .Replace("'e", " ")
            .Replace("'a", " ")
            .Replace("'nin", " ")
            .Replace("'nın", " ")
            .Replace("'nun", " ")
            .Replace("'nün", " ")
            .Replace("'in", " ")
            .Replace("'ın", " ")
            .Replace("'un", " ")
            .Replace("'ün", " ")
            .Replace(".", " ")
            .Replace(",", " ")
            .Replace("?", " ")
            .Replace("!", " ")
            .Replace(":", " ")
            .Replace(";", " ")
            .Replace("(", " ")
            .Replace(")", " ");

        value = value
            .Replace("â", "a")
            .Replace("î", "i")
            .Replace("û", "u");

        value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();

        return value;
    }

    private static bool ContainsWholePhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
            return false;

        var pattern = $@"(^|\s){System.Text.RegularExpressions.Regex.Escape(phrase)}(\s|$)";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
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
            "ay", "ayında", "ayinda", "bu", "geçen", "gecen",
            "ne", "kadar", "kaç", "tl", "para", "mi", "mı", "mu", "mü",
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
            "bugün", "bugun", "dün", "dun", "bu", "geçen", "gecen", "ay");
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
