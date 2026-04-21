using FirmovaAI.Data;
using FirmovaAI.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FirmovaAI.Services
{
    public class OpenAiService
    {
        private readonly EskiMuhasebeDbContext _db;
        private readonly FirmovaAiDbContext _aiDb;
        private readonly SqliteCariService _cariService;
        private readonly SqliteCalisanService _calisanService;
        private readonly SqliteCalisanHareketService _calisanHareketService;
        private readonly SqliteYoneticiOzetService _yoneticiOzetService;
        
        // Basit konuşma hafızası.
        // Tek kullanıcı / demo kullanım için uygundur.
        private static string _sonKonusulanCalisan = "";

        public OpenAiService(
            EskiMuhasebeDbContext db,
            FirmovaAiDbContext aiDb,
            SqliteCariService cariService,
            SqliteCalisanService calisanService,
            SqliteCalisanHareketService calisanHareketService,
            SqliteYoneticiOzetService yoneticiOzetService)
        {
            _db = db;
            _aiDb = aiDb;
            _cariService = cariService;
            _calisanService = calisanService;
            _calisanHareketService = calisanHareketService;
            _yoneticiOzetService = yoneticiOzetService;
        }

        public async Task<string> SorAsync(string soru)
        {
            var cevap = await GercekDemoCevap(soru);

            var log = new ChatLog
            {
                Soru = soru,
                Cevap = cevap,
                Tarih = DateTime.Now
            };

            _aiDb.ChatLogs.Add(log);
            await _aiDb.SaveChangesAsync();

            return cevap;
        }

        private async Task<string> GercekDemoCevap(string soru)
        {
            string orijinalSoru = soru ?? "";
            string normalizeSoru = NormalizeText(soru);

            if (string.IsNullOrWhiteSpace(normalizeSoru))
                return "Lütfen bir soru yazın.";

            if (ContainsAny(normalizeSoru, "merhaba", "selam", "hey", "iyi gunler", "iyi aksamlar"))
                return "Merhaba, sana yardımcı olmaya hazırım.";

            // Son konuşulan çalışanı güncelle
            var tespitEdilenCalisan = await TespitEdilenCalisaniBulAsync(orijinalSoru);
            if (!string.IsNullOrWhiteSpace(tespitEdilenCalisan))
                _sonKonusulanCalisan = tespitEdilenCalisan;

            // TABLO / SÜTUN GÖSTERME
            if (ContainsAny(normalizeSoru, "cari sutun", "cari sütun", "cari alan", "cari kolon"))
            {
                var columns = await _cariService.GetTableColumnsAsync("CariKartlar");
                if (columns.Count == 0)
                    return "CariKartlar tablosu bulunamadı ya da sütunlar okunamadı.";

                return "CariKartlar tablosundaki sütunlar:\n- " + string.Join("\n- ", columns);
            }

            if (ContainsAny(normalizeSoru, "calisan sutun", "çalışan sütun", "calisan alan", "çalışan alan", "personel sutun", "personel alan"))
            {
                var columns = await _calisanService.GetTableColumnsAsync("Calisanlar");
                if (columns.Count == 0)
                    return "Calisanlar tablosu bulunamadı ya da sütunlar okunamadı.";

                return "Calisanlar tablosundaki sütunlar:\n- " + string.Join("\n- ", columns);
            }

            if (ContainsAny(normalizeSoru, "calisan hareket sutun", "çalışan hareket sütun", "calisan hareket alan", "çalışan hareket alan"))
            {
                var columns = await _calisanHareketService.GetTableColumnsAsync("CalisanAvanslar");
                if (columns.Count == 0)
                    return "CalisanAvanslar tablosu bulunamadı ya da sütunlar okunamadı.";

                return "CalisanAvanslar tablosundaki sütunlar:\n- " + string.Join("\n- ", columns);
            }

            if (ContainsAny(normalizeSoru, "stok sutun", "stok sütun", "stok alan", "stok kolon", "urun sutun", "ürün sütun"))
            {
                var columns = await _cariService.GetTableColumnsAsync("StokUrunler");
                if (columns.Count == 0)
                    return "StokUrunler tablosu bulunamadı ya da sütunlar okunamadı.";

                return "StokUrunler tablosundaki sütunlar:\n- " + string.Join("\n- ", columns);
            }

            // YÖNETİCİ ÖZET / RİSK / ZAMAN FİLTRELİ SORGULAR
            if (ContainsAny(normalizeSoru, "bugunun ozeti", "bugünün özeti", "bugun ne olmus", "bugün ne olmuş"))
            {
                return await _yoneticiOzetService.BugununOzetiAsync();
            }

            if (ContainsAny(normalizeSoru, "bu ay ozeti", "bu ay özeti", "aylik ozet", "aylık özet"))
            {
                return await _yoneticiOzetService.BuAyOzetiAsync();
            }

            if (ContainsAny(normalizeSoru, "riskli durumlar", "risk var mi", "risk var mı"))
            {
                return await _yoneticiOzetService.RiskliDurumlarAsync();
            }

            if (ContainsAny(normalizeSoru, "kritik stoklar", "kritik stok var mi", "kritik stok var mı"))
            {
                return await _yoneticiOzetService.KritikStoklarAsync();
            }

            if (ContainsAny(normalizeSoru, "yuksek kasa cikislari", "yüksek kasa çıkışları", "buyuk kasa cikislari", "büyük kasa çıkışları"))
            {
                return await _yoneticiOzetService.YuksekKasaCikislariAsync();
            }

            if (ContainsAny(normalizeSoru, "son 7 gun calisan hareketleri", "son 7 gün çalışan hareketleri"))
            {
                return await _yoneticiOzetService.SonGunCalisanHareketleriAsync(7);
            }

            if (ContainsAny(normalizeSoru, "son 30 gun calisan hareketleri", "son 30 gün çalışan hareketleri"))
            {
                return await _yoneticiOzetService.SonGunCalisanHareketleriAsync(30);
            }

            // TEK ÇALIŞAN TAM ÖZET
            if (ContainsAny(normalizeSoru, "tam ozet", "tam özet", "tum bilgiler", "tüm bilgiler", "tam bilgi"))
            {
                var calisanAdi = await KonusulanCalisaniGetirAsync(orijinalSoru);
                if (string.IsNullOrWhiteSpace(calisanAdi))
                    return "Hangi çalışan için tam özet istediğini anlayamadım. Örnek: 'Ahmet tam özet'.";

                var columns = await _calisanService.GetTableColumnsAsync("Calisanlar");
                var row = await _calisanService.GetCalisanDetayAsync(calisanAdi);

                if (row == null)
                    return $"'{calisanAdi}' için çalışan bulunamadı.";

                var detay = _calisanService.FormatCalisanDetay(row, columns);
                var sonAvans = await _calisanHareketService.KisiyeAitSonHareketAsync(calisanAdi, "avans");
                var sonMaas = await _calisanHareketService.KisiyeAitSonHareketAsync(calisanAdi, "maas");
                var toplamAvans = await _calisanHareketService.KisiyeAitToplamAvansAsync(calisanAdi);

                return $"{RastgeleBaslangic()}\n\n{calisanAdi} için topladığım özet:\n\n{detay}\n\n{sonAvans}\n\n{sonMaas}\n\n{toplamAvans}";
            }

            // TAKİP SORULARI - HAFIZA
            if (ContainsAny(normalizeSoru, "maasi ne", "maaşı ne", "maasi kac", "maaşı kaç", "son maasi", "son maaşı"))
            {
                var calisanAdi = await KonusulanCalisaniGetirAsync(orijinalSoru);
                if (string.IsNullOrWhiteSpace(calisanAdi))
                    return "Hangi çalışandan bahsettiğini anlayamadım. Önce çalışan adını yazabilirsin.";

                return $"{RastgeleBaslangic()}\n" + await _calisanHareketService.KisiyeAitSonHareketAsync(calisanAdi, "maas");
            }

            if (ContainsAny(normalizeSoru, "avansi ne", "avansı ne", "son avansi", "son avansı", "avansi kac", "avansı kaç"))
            {
                var calisanAdi = await KonusulanCalisaniGetirAsync(orijinalSoru);
                if (string.IsNullOrWhiteSpace(calisanAdi))
                    return "Hangi çalışandan bahsettiğini anlayamadım. Önce çalışan adını yazabilirsin.";

                return $"{RastgeleBaslangic()}\n" + await _calisanHareketService.KisiyeAitSonHareketAsync(calisanAdi, "avans");
            }

            if (ContainsAny(normalizeSoru, "telefonu ne", "telefon numarasi ne", "telefon numarası ne", "telefonunu soyle", "telefonunu söyle"))
            {
                var calisanAdi = await KonusulanCalisaniGetirAsync(orijinalSoru);
                if (string.IsNullOrWhiteSpace(calisanAdi))
                    return "Hangi çalışandan bahsettiğini anlayamadım. Önce çalışan adını yazabilirsin.";

                var columns = await _calisanService.GetTableColumnsAsync("Calisanlar");
                var row = await _calisanService.GetCalisanDetayAsync(calisanAdi);

                if (row == null)
                    return $"'{calisanAdi}' için çalışan bulunamadı.";

                var telefonKolon = _calisanService.FindFirstExisting(columns, "Telefon", "Telefon1", "CepTelefonu", "Gsm", "Cep");
                if (string.IsNullOrWhiteSpace(telefonKolon) || !row.ContainsKey(telefonKolon) || string.IsNullOrWhiteSpace(row[telefonKolon]?.ToString()))
                    return $"{calisanAdi} için telefon bilgisi bulunamadı.";

                return $"{RastgeleBaslangic()}\n{calisanAdi} için telefon bilgisi: {row[telefonKolon]}";
            }

            if (ContainsAny(normalizeSoru, "ise giris tarihi ne", "işe giriş tarihi ne", "ne zaman ise girdi", "ne zaman işe girdi"))
            {
                var calisanAdi = await KonusulanCalisaniGetirAsync(orijinalSoru);
                if (string.IsNullOrWhiteSpace(calisanAdi))
                    return "Hangi çalışandan bahsettiğini anlayamadım. Önce çalışan adını yazabilirsin.";

                var columns = await _calisanService.GetTableColumnsAsync("Calisanlar");
                var row = await _calisanService.GetCalisanDetayAsync(calisanAdi);

                if (row == null)
                    return $"'{calisanAdi}' için çalışan bulunamadı.";

                var tarihKolon = _calisanService.FindFirstExisting(columns, "IseGirisTarihi", "İseGirisTarihi", "GirisTarihi", "IseBaslamaTarihi");
                if (string.IsNullOrWhiteSpace(tarihKolon) || !row.ContainsKey(tarihKolon) || string.IsNullOrWhiteSpace(row[tarihKolon]?.ToString()))
                    return $"{calisanAdi} için işe giriş tarihi bulunamadı.";

                return $"{RastgeleBaslangic()}\n{calisanAdi} için işe giriş tarihi: {row[tarihKolon]}";
            }

            // DETAY SORGULARI
            if (ContainsAny(normalizeSoru, "calisan detay", "çalışan detay", "calisan bilgisi", "çalışan bilgisi", "personel detay", "personel bilgisi"))
            {
                var aranan = await KonusulanCalisaniGetirAsync(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi çalışan için detay istediğini anlayamadım. Örnek: 'Ahmet çalışan detay'.";

                var columns = await _calisanService.GetTableColumnsAsync("Calisanlar");
                var row = await _calisanService.GetCalisanDetayAsync(aranan);

                if (row == null)
                    return $"'{aranan}' için çalışan bulunamadı.";

                return _calisanService.FormatCalisanDetay(row, columns) + "\n\nİstersen son avansını, son maaşını ya da tam özetini de gösterebilirim.";
            }

            if (ContainsAny(normalizeSoru, "cari detay", "cari bilgisi", "musteri detay", "müşteri bilgisi", "firma bilgisi"))
            {
                var aranan = ArananMetniBul(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi cari için detay istediğini anlayamadım. Örnek: 'Yılmaz cari detay'.";

                var columns = await _cariService.GetTableColumnsAsync("CariKartlar");
                var row = await _cariService.GetCariDetayAsync(aranan);

                if (row == null)
                    return $"'{aranan}' için cari bulunamadı.";

                return _cariService.FormatCariDetay(row, columns);
            }

            if (ContainsAny(normalizeSoru, "stok detay", "urun detay", "ürün detay", "urun bilgisi", "ürün bilgisi"))
            {
                var aranan = ArananMetniBul(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi ürün için detay istediğini anlayamadım. Örnek: 'Masa stok detay'.";

                var columns = await _cariService.GetTableColumnsAsync("StokUrunler");
                var row = await _cariService.GetStokDetayAsync(aranan);

                if (row == null)
                    return $"'{aranan}' için stok ürünü bulunamadı.";

                return _cariService.FormatStokDetay(row, columns);
            }

            // ÇALIŞAN HAREKET SORGULARI
            if (ContainsAny(normalizeSoru, "en son kime avans verdim", "son avans verdigim kisi", "son avans verdiğim kişi"))
                return await _calisanHareketService.EnSonAvansVerilenKisiAsync();

            if (ContainsAny(normalizeSoru, "en son ne zaman avans", "son avans tarihi", "en son avans ne zaman"))
            {
                var aranan = await KonusulanCalisaniGetirAsync(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi çalışan için avans tarihini istediğini anlayamadım. Örnek: 'Ahmet için en son ne zaman avans verdim'.";

                return await _calisanHareketService.KisiyeAitSonHareketAsync(aranan, "avans");
            }

            if (ContainsAny(normalizeSoru, "en son ne zaman maas", "en son ne zaman maaş", "son maas tarihi", "son maaş tarihi", "en son maaş ne zaman"))
            {
                var aranan = await KonusulanCalisaniGetirAsync(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi çalışan için maaş tarihini istediğini anlayamadım. Örnek: 'Ahmet için en son ne zaman maaş ödedim'.";

                return await _calisanHareketService.KisiyeAitSonHareketAsync(aranan, "maas");
            }

            if (ContainsAny(normalizeSoru, "toplam ne kadar avans aldi", "toplam ne kadar avans aldı", "toplam avans ne kadar"))
            {
                var aranan = await KonusulanCalisaniGetirAsync(orijinalSoru);

                if (string.IsNullOrWhiteSpace(aranan))
                    return "Hangi çalışan için toplam avans bilgisini istediğini anlayamadım. Örnek: 'Ahmet toplam ne kadar avans aldı'.";

                return await _calisanHareketService.KisiyeAitToplamAvansAsync(aranan);
            }

            if (ContainsAny(normalizeSoru, "bu ay kimlere maas odedim", "bu ay kimlere maaş ödedim"))
                return await _calisanHareketService.BuAyKimlereMaasOdendiAsync();

            if (ContainsAny(normalizeSoru, "bu ay kimlere avans verdim", "bu ay avans verdigim kisiler", "bu ay avans verdiğim kişiler"))
                return await _calisanHareketService.BuAyKimlereAvansVerildiAsync();

            if (ContainsAny(normalizeSoru, "en cok avans verdigim calisan kim", "en çok avans verdiğim çalışan kim"))
                return await _calisanHareketService.EnCokAvansVerilenCalisanAsync();

            if (ContainsAny(normalizeSoru, "toplam maas odemesi ne kadar", "toplam maaş ödemesi ne kadar", "toplam maas ne kadar", "toplam maaş ne kadar"))
                return await _calisanHareketService.ToplamMaasOdemesiAsync();

            if (ContainsAny(normalizeSoru, "son 5 calisan hareketi", "son 5 çalışan hareketi", "son calisan hareketleri", "son çalışan hareketleri"))
                return await _calisanHareketService.SonCalisanHareketleriAsync(5);

            var konu = KonuBul(normalizeSoru);
            var islem = IslemBul(normalizeSoru);

            if (konu == "bilinmiyor")
                return "Sorunun hangi alanla ilgili olduğunu anlayamadım. Çalışan, cari, stok veya kasa ile ilgili daha net yazabilirsin.";

            if (islem == "ara")
            {
                var arananMetin = ArananMetniBul(orijinalSoru);

                if (string.IsNullOrWhiteSpace(arananMetin))
                    return "Aramak istediğin ismi ya da kelimeyi anlayamadım. Örnek: 'Ahmet diye çalışan var mı', 'Yılmaz diye cari var mı' veya 'masa isimli ürün var mı'.";

                if (konu == "calisan")
                {
                    var adaylar = await _db.Calisanlar
                        .Select(x => x.AdSoyad)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToListAsync();

                    return AramaSonucuOlustur("çalışan", arananMetin, adaylar);
                }

                if (konu == "cari")
                {
                    var adaylar = await _db.CariKartlar
                        .Select(x => x.Unvan)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToListAsync();

                    return AramaSonucuOlustur("cari", arananMetin, adaylar);
                }

                if (konu == "stok")
                {
                    var adaylar = await _db.StokUrunler
                        .Select(x => x.Ad)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToListAsync();

                    return AramaSonucuOlustur("stok ürünü", arananMetin, adaylar);
                }

                if (konu == "kasa")
                    return "Kasa alanında isim bazlı arama henüz tanımlanmadı.";
            }

            if (islem == "bilinmiyor")
                return "Soruda ne yapmak istediğini anlayamadım. Sayı mı istiyorsun, liste mi istiyorsun, arama mı yapmak istiyorsun, biraz daha açık yazabilirsin.";

            if (konu == "calisan")
            {
                if (islem == "say")
                {
                    var toplam = await _db.Calisanlar.CountAsync();

                    var liste = await _db.Calisanlar
                        .OrderBy(x => x.Id)
                        .Take(5)
                        .Select(x => x.AdSoyad)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı çalışan bulunamadı.";

                    return $"Toplam çalışan sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }

                if (islem == "liste")
                {
                    int adet = ListeAdediBul(normalizeSoru, 10);

                    var toplam = await _db.Calisanlar.CountAsync();

                    var liste = await _db.Calisanlar
                        .OrderBy(x => x.Id)
                        .Take(adet)
                        .Select(x => x.AdSoyad)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı çalışan bulunamadı.";

                    return $"Toplam çalışan sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }
            }

            if (konu == "cari")
            {
                if (islem == "say")
                {
                    var toplam = await _db.CariKartlar.CountAsync();

                    var liste = await _db.CariKartlar
                        .OrderBy(x => x.Id)
                        .Take(5)
                        .Select(x => x.Unvan)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı cari bulunamadı.";

                    return $"Toplam cari sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }

                if (islem == "liste")
                {
                    int adet = ListeAdediBul(normalizeSoru, 10);

                    var toplam = await _db.CariKartlar.CountAsync();

                    var liste = await _db.CariKartlar
                        .OrderBy(x => x.Id)
                        .Take(adet)
                        .Select(x => x.Unvan)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı cari bulunamadı.";

                    return $"Toplam cari sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }
            }

            if (konu == "stok")
            {
                if (islem == "say")
                {
                    var toplam = await _db.StokUrunler.CountAsync();

                    var liste = await _db.StokUrunler
                        .OrderBy(x => x.Id)
                        .Take(5)
                        .Select(x => x.Ad)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı stok ürünü bulunamadı.";

                    return $"Toplam stok ürün sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }

                if (islem == "liste")
                {
                    int adet = ListeAdediBul(normalizeSoru, 10);

                    var toplam = await _db.StokUrunler.CountAsync();

                    var liste = await _db.StokUrunler
                        .OrderBy(x => x.Id)
                        .Take(adet)
                        .Select(x => x.Ad)
                        .ToListAsync();

                    if (liste.Count == 0)
                        return "Kayıtlı stok ürünü bulunamadı.";

                    return $"Toplam stok ürün sayısı: {toplam}\nGösterilen: {liste.Count}\n- " +
                           string.Join("\n- ", liste);
                }
            }

            if (konu == "kasa")
            {
                if (islem == "say" || islem == "liste")
                {
                    try
                    {
                        var toplam = await _db.KasaHareketler.CountAsync();
                        return $"Toplam kasa hareket sayısı: {toplam}";
                    }
                    catch
                    {
                        return "Kasa hareket tablosu bu veritabanında bulunamadı ya da tablo adı farklı.";
                    }
                }
            }

            return "Bu soru için henüz uygun bir cevap oluşturulamadı.";
        }

        private async Task<string> TespitEdilenCalisaniBulAsync(string orijinalSoru)
        {
            var aday = ArananMetniBul(orijinalSoru);
            if (string.IsNullOrWhiteSpace(aday))
                return "";

            var bulunan = await _db.Calisanlar
                .Where(x => !string.IsNullOrWhiteSpace(x.AdSoyad) && x.AdSoyad.ToLower().Contains(aday.ToLower()))
                .OrderBy(x => x.AdSoyad)
                .Select(x => x.AdSoyad)
                .FirstOrDefaultAsync();

            return bulunan ?? "";
        }

        private async Task<string> KonusulanCalisaniGetirAsync(string orijinalSoru)
        {
            var tespit = await TespitEdilenCalisaniBulAsync(orijinalSoru);
            if (!string.IsNullOrWhiteSpace(tespit))
            {
                _sonKonusulanCalisan = tespit;
                return tespit;
            }

            return _sonKonusulanCalisan;
        }

        private string RastgeleBaslangic()
        {
            var secenekler = new[]
            {
                "Tamam, hemen kontrol ediyorum.",
                "Elbette, senin için baktım.",
                "İşte bulduklarım.",
                "Bir saniye, hemen bakıyorum.",
                "Hemen kontrol ediyorum."
            };

            return secenekler[Random.Shared.Next(secenekler.Length)];
        }

        private string AramaSonucuOlustur(string alanAdi, string arananMetin, List<string> adaylar)
        {
            if (adaylar.Count == 0)
                return $"Kayıtlı {alanAdi} bulunamadı.";

            string normalizeAranan = NormalizeText(arananMetin);
            var arananKelimeler = KelimelereAyir(normalizeAranan);

            var hazirListe = adaylar
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => new AdaySonuc
                {
                    Orijinal = x,
                    Normalize = NormalizeText(x),
                    Kelimeler = KelimelereAyir(NormalizeText(x))
                })
                .ToList();

            var tamEslesenler = hazirListe
                .Where(x => x.Normalize == normalizeAranan || x.Kelimeler.Contains(normalizeAranan))
                .OrderBy(x => x.Orijinal)
                .Take(10)
                .ToList();

            if (tamEslesenler.Count > 0)
            {
                return $"Aranan ifade: {arananMetin}\nTam eşleşen {alanAdi} sayısı: {tamEslesenler.Count}\n- " +
                       string.Join("\n- ", tamEslesenler.Select(x => x.Orijinal));
            }

            var benzerler = hazirListe
                .Select(x => new
                {
                    x.Orijinal,
                    Puan = BenzerlikPuaniHesapla(normalizeAranan, arananKelimeler, x.Normalize, x.Kelimeler)
                })
                .Where(x => x.Puan >= 180)
                .OrderByDescending(x => x.Puan)
                .ThenBy(x => x.Orijinal)
                .Take(10)
                .ToList();

            if (benzerler.Count == 0)
                return $"Aranan ifade: {arananMetin}\nTam eşleşme bulunamadı.\nBenzer sonuç da bulunamadı.";

            return $"Aranan ifade: {arananMetin}\nTam eşleşme bulunamadı.\nBenzer sonuçlar ({benzerler.Count} kayıt):\n- " +
                   string.Join("\n- ", benzerler.Select(x => x.Orijinal));
        }

        private int BenzerlikPuaniHesapla(string normalizeAranan, List<string> arananKelimeler, string normalizeAday, List<string> adayKelimeler)
        {
            int puan = 0;

            if (string.IsNullOrWhiteSpace(normalizeAranan) || string.IsNullOrWhiteSpace(normalizeAday))
                return 0;

            if (normalizeAday == normalizeAranan)
                return 1000;

            if (adayKelimeler.Contains(normalizeAranan))
                puan += 500;

            if (normalizeAday.StartsWith(normalizeAranan))
                puan += 220;

            if (normalizeAday.Contains(normalizeAranan) && normalizeAranan.Length >= 4)
                puan += 140;

            foreach (var kelime in arananKelimeler)
            {
                if (string.IsNullOrWhiteSpace(kelime))
                    continue;

                if (adayKelimeler.Contains(kelime))
                {
                    puan += 220;
                    continue;
                }

                if (adayKelimeler.Any(x => x.StartsWith(kelime)))
                {
                    puan += 130;
                    continue;
                }

                if (kelime.Length >= 5 && adayKelimeler.Any(x => x.Contains(kelime)))
                {
                    puan += 80;
                    continue;
                }

                if (kelime.Length >= 5)
                {
                    int minMesafe = adayKelimeler.Select(x => LevenshteinDistance(kelime, x)).DefaultIfEmpty(999).Min();
                    if (minMesafe == 1)
                        puan += 35;
                }
            }

            if (arananKelimeler.Count > 1)
            {
                int tamEslesenKelimeSayisi = arananKelimeler.Count(k => adayKelimeler.Contains(k));
                puan += tamEslesenKelimeSayisi * 70;
            }

            return puan;
        }

        private int ListeAdediBul(string soru, int varsayilanDeger)
        {
            if (ContainsAny(soru, "ilk 3", "3 tane", "3 adet")) return 3;
            if (ContainsAny(soru, "ilk 5", "5 tane", "5 adet")) return 5;
            if (ContainsAny(soru, "ilk 10", "10 tane", "10 adet")) return 10;
            if (ContainsAny(soru, "ilk 20", "20 tane", "20 adet")) return 20;

            return varsayilanDeger;
        }

        private List<string> KelimelereAyir(string text)
        {
            return (text ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }

            return d[s.Length, t.Length];
        }

        private string KonuBul(string soru)
        {
            if (ContainsAny(soru, "calisan", "çalışan", "personel", "ekip", "isci", "işçi")) return "calisan";
            if (ContainsAny(soru, "cari", "musteri", "müşteri", "firma", "müsteri", "cari kart")) return "cari";
            if (ContainsAny(soru, "stok", "urun", "ürün", "malzeme", "depo")) return "stok";
            if (ContainsAny(soru, "kasa", "hareket", "kasa hareket", "tahsilat", "odeme", "ödeme")) return "kasa";

            return "bilinmiyor";
        }

        private string IslemBul(string soru)
        {
            if (ContainsAny(soru, "var mi", "var mı", "arama", "ara", "bul", "gecen", "geçen", "isimli", "adli", "adlı", "diye"))
                return "ara";

            if (ContainsAny(soru, "kac", "kaç", "say", "sayi", "sayisi", "sayısı", "toplam", "ne kadar", "kaç tane", "kaç kişi"))
                return "say";

            if (ContainsAny(soru, "listele", "goster", "göster", "yazdir", "yazdır", "sirala", "sırala", "gosterir misin", "liste"))
                return "liste";

            return "bilinmiyor";
        }

        private string ArananMetniBul(string soru)
        {
            if (string.IsNullOrWhiteSpace(soru))
                return "";

            string[] kaliplar =
            {
                " diye çalışan var mı"," diye calisan var mi"," diye çalışan"," diye calisan",
                " isimli çalışan var mı"," isimli calisan var mi",
                " isimli ürün var mı"," isimli urun var mi",
                " isimli cari var mı"," isimli cari var mi",
                " adlı çalışan var mı"," adli calisan var mi",
                " adlı cari var mı"," adli cari var mi",
                " adlı ürün var mı"," adli urun var mi",
                " geçen çalışan var mı"," gecen calisan var mi",
                " geçen müşteri var mı"," gecen musteri var mi",
                " geçen cari var mı"," gecen cari var mi",
                " geçen ürün var mı"," gecen urun var mi",
                " cari detay"," cari bilgisi"," müşteri bilgisi"," musteri bilgisi"," firma bilgisi",
                " stok detay"," stok bilgisi"," urun detay"," ürün detay"," urun bilgisi"," ürün bilgisi",
                " calisan detay"," çalışan detay"," calisan bilgisi"," çalışan bilgisi"," personel detay"," personel bilgisi",
                " icin en son ne zaman avans verdim"," için en son ne zaman avans verdim",
                " icin son avans tarihi"," için son avans tarihi",
                " icin en son ne zaman maas odedim"," için en son ne zaman maaş ödedim",
                " icin son maas tarihi"," için son maaş tarihi",
                " toplam ne kadar avans aldi"," toplam ne kadar avans aldı"," toplam avans ne kadar",
                " tam ozet"," tam özet"," tum bilgiler"," tüm bilgiler"," tam bilgi"
            };

            string temiz = soru.Trim();

            foreach (var kalip in kaliplar)
            {
                int index = temiz.ToLowerInvariant().IndexOf(kalip);
                if (index > 0)
                    return temiz.Substring(0, index).Trim().Trim('\'', '"');
            }

            string[] anlamsizKelimeler =
            {
                "çalışan","calisan","personel","ekip",
                "cari","müşteri","musteri","firma","carikart","cari kart",
                "ürün","urun","stok","malzeme",
                "var","mı","mi","diye","isimli","adlı","adli",
                "ara","bul","gecen","geçen",
                "detay","bilgisi","bilgi",
                "icin","için","en","son","ne","zaman","avans","verdim","maas","maaş","odedim","ödedim","tarihi",
                "toplam","kadar","aldi","aldı","bu","ay","kimlere","en","cok","çok","odenen","ödenen",
                "hareketleri","hareketi","tam","ozet","özet","tum","tüm"
            };

            var parcalar = temiz
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !anlamsizKelimeler.Contains(x.ToLowerInvariant()))
                .ToList();

            return string.Join(" ", parcalar).Trim();
        }

        private bool ContainsAny(string text, params string[] values)
        {
            return values.Any(v => text.Contains(v));
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string lower = text.Trim().ToLowerInvariant();
            string normalized = lower.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder();

            foreach (char c in normalized)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString()
                .Replace('ı', 'i')
                .Replace('ğ', 'g')
                .Replace('ü', 'u')
                .Replace('ş', 's')
                .Replace('ö', 'o')
                .Replace('ç', 'c');
        }

        private class AdaySonuc
        {
            public string Orijinal { get; set; } = "";
            public string Normalize { get; set; } = "";
            public List<string> Kelimeler { get; set; } = new();
        }
    }
}