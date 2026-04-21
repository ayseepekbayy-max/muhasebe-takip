using Microsoft.Data.Sqlite;

namespace FirmovaAI.Services
{
    public class SqliteYoneticiOzetService
    {
        private readonly string _connectionString;

        public SqliteYoneticiOzetService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("EskiMuhasebeDb") ?? "";
        }

        public async Task<List<string>> GetTableColumnsAsync(string tableName)
        {
            var sonuc = new List<string>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}])";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sonuc.Add(reader["name"]?.ToString() ?? "");
            }

            return sonuc;
        }

        public string FindFirstExisting(List<string> columns, params string[] adaylar)
        {
            foreach (var aday in adaylar)
            {
                var bulunan = columns.FirstOrDefault(x =>
                    x.Equals(aday, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(bulunan))
                    return bulunan;
            }

            return "";
        }

        public async Task<string> BugununOzetiAsync()
        {
            var parcalar = new List<string>();

            var kasa = await KasaOzetAsync("date", "Bugün");
            if (!string.IsNullOrWhiteSpace(kasa))
                parcalar.Add(kasa);

            var calisan = await CalisanHareketOzetAsync("date", "Bugün");
            if (!string.IsNullOrWhiteSpace(calisan))
                parcalar.Add(calisan);

            var stok = await StokHareketOzetAsync("date", "Bugün");
            if (!string.IsNullOrWhiteSpace(stok))
                parcalar.Add(stok);

            if (parcalar.Count == 0)
                return "Bugün için özet oluşturulamadı.";

            return "Bugünün özeti:\n\n" + string.Join("\n\n", parcalar);
        }

        public async Task<string> BuAyOzetiAsync()
        {
            var parcalar = new List<string>();

            var kasa = await KasaOzetAsync("month", "Bu ay");
            if (!string.IsNullOrWhiteSpace(kasa))
                parcalar.Add(kasa);

            var calisan = await CalisanHareketOzetAsync("month", "Bu ay");
            if (!string.IsNullOrWhiteSpace(calisan))
                parcalar.Add(calisan);

            var stok = await StokHareketOzetAsync("month", "Bu ay");
            if (!string.IsNullOrWhiteSpace(stok))
                parcalar.Add(stok);

            if (parcalar.Count == 0)
                return "Bu ay için özet oluşturulamadı.";

            return "Bu ay özeti:\n\n" + string.Join("\n\n", parcalar);
        }

        public async Task<string> RiskliDurumlarAsync()
        {
            var riskler = new List<string>();

            var kritikStok = await KritikStoklarListeAsync(3);
            if (kritikStok.Any())
                riskler.Add("Kritik stoklar:\n- " + string.Join("\n- ", kritikStok));

            var yuksekCikis = await YuksekKasaCikislariListeAsync(3);
            if (yuksekCikis.Any())
                riskler.Add("Yüksek kasa çıkışları:\n- " + string.Join("\n- ", yuksekCikis));

            var fazlaAvans = await EnCokAvansVerilenCalisanRawAsync();
            if (!string.IsNullOrWhiteSpace(fazlaAvans))
                riskler.Add(fazlaAvans);

            if (riskler.Count == 0)
                return "Şu an belirgin bir riskli durum görünmüyor.";

            return "Riskli durumlar:\n\n" + string.Join("\n\n", riskler);
        }

        public async Task<string> KritikStoklarAsync()
        {
            var liste = await KritikStoklarListeAsync(10);

            if (liste.Count == 0)
                return "Kritik stok görünmüyor.";

            return "Kritik stoklar:\n- " + string.Join("\n- ", liste);
        }

        public async Task<string> YuksekKasaCikislariAsync()
        {
            var liste = await YuksekKasaCikislariListeAsync(10);

            if (liste.Count == 0)
                return "Yüksek kasa çıkışı bulunamadı.";

            return "Yüksek kasa çıkışları:\n- " + string.Join("\n- ", liste);
        }

        public async Task<string> SonGunCalisanHareketleriAsync(int gun)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslar");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0 || calisanColumns.Count == 0)
                return "Çalışan hareket tabloları bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "HareketTipi", "Tip");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "Son çalışan hareketleri için gerekli sütunlar bulunamadı.";
            }

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       h.[{tipKolon}] AS HareketTipi,
       h.[{tarihKolon}] AS Tarih,
       {BuildSafeSelect("h", tutarKolon, "Tutar")}
FROM [CalisanAvanslar] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE date(h.[{tarihKolon}]) >= date('now', 'localtime', @gun)
ORDER BY h.[{tarihKolon}] DESC
LIMIT 20";

            cmd.Parameters.AddWithValue("@gun", $"-{gun} day");

            using var reader = await cmd.ExecuteReaderAsync();
            var satirlar = new List<string>();

            while (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? "Bilinmeyen çalışan";
                string tip = reader["HareketTipi"]?.ToString() ?? "-";
                string tarih = reader["Tarih"]?.ToString() ?? "-";
                string tutar = reader["Tutar"]?.ToString() ?? "-";

                satirlar.Add($"{ad} | {tip} | {tarih} | {tutar}");
            }

            if (satirlar.Count == 0)
                return $"Son {gun} gün için çalışan hareketi bulunamadı.";

            return $"Son {gun} gün çalışan hareketleri:\n- " + string.Join("\n- ", satirlar);
        }

        private async Task<string> KasaOzetAsync(string mode, string baslik)
        {
            var columns = await GetTableColumnsAsync("KasaHareketler");
            if (columns.Count == 0)
                return "";

            string tipKolon = FindFirstExisting(columns, "HareketTipi", "Tip");
            string tutarKolon = FindFirstExisting(columns, "Tutar", "Miktar");
            string tarihKolon = FindFirstExisting(columns, "Tarih", "IslemTarihi");

            if (string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tutarKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon))
                return "";

            string filtre = mode == "date"
                ? $"date([{tarihKolon}]) = date('now', 'localtime')"
                : $"strftime('%Y-%m', [{tarihKolon}]) = strftime('%Y-%m', 'now', 'localtime')";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT
    COUNT(*) AS ToplamHareket,
    IFNULL(SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%giris%' THEN CAST(IFNULL([{tutarKolon}], 0) AS REAL) ELSE 0 END), 0) AS GirisToplam,
    IFNULL(SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%cikis%' THEN CAST(IFNULL([{tutarKolon}], 0) AS REAL) ELSE 0 END), 0) AS CikisToplam
FROM [KasaHareketler]
WHERE {filtre}";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return $"{baslik} kasa özeti:\nToplam Hareket: {reader["ToplamHareket"]}\nGiriş Toplamı: {reader["GirisToplam"]}\nÇıkış Toplamı: {reader["CikisToplam"]}";
            }

            return "";
        }

        private async Task<string> CalisanHareketOzetAsync(string mode, string baslik)
        {
            var columns = await GetTableColumnsAsync("CalisanAvanslar");
            if (columns.Count == 0)
                return "";

            string tipKolon = FindFirstExisting(columns, "HareketTipi", "Tip");
            string tarihKolon = FindFirstExisting(columns, "Tarih", "IslemTarihi");

            if (string.IsNullOrWhiteSpace(tipKolon) || string.IsNullOrWhiteSpace(tarihKolon))
                return "";

            string filtre = mode == "date"
                ? $"date([{tarihKolon}]) = date('now', 'localtime')"
                : $"strftime('%Y-%m', [{tarihKolon}]) = strftime('%Y-%m', 'now', 'localtime')";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT
    COUNT(*) AS ToplamHareket,
    SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%avans%' THEN 1 ELSE 0 END) AS AvansSayisi,
    SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%maas%' THEN 1 ELSE 0 END) AS MaasSayisi
FROM [CalisanAvanslar]
WHERE {filtre}";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return $"{baslik} çalışan hareket özeti:\nToplam Hareket: {reader["ToplamHareket"]}\nAvans Sayısı: {reader["AvansSayisi"]}\nMaaş Sayısı: {reader["MaasSayisi"]}";
            }

            return "";
        }

        private async Task<string> StokHareketOzetAsync(string mode, string baslik)
        {
            var columns = await GetTableColumnsAsync("StokHareketler");
            if (columns.Count == 0)
                return "";

            string tipKolon = FindFirstExisting(columns, "HareketTipi", "Tip");
            string tarihKolon = FindFirstExisting(columns, "Tarih", "IslemTarihi");

            if (string.IsNullOrWhiteSpace(tipKolon) || string.IsNullOrWhiteSpace(tarihKolon))
                return "";

            string filtre = mode == "date"
                ? $"date([{tarihKolon}]) = date('now', 'localtime')"
                : $"strftime('%Y-%m', [{tarihKolon}]) = strftime('%Y-%m', 'now', 'localtime')";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT
    COUNT(*) AS ToplamHareket,
    SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%giris%' THEN 1 ELSE 0 END) AS GirisSayisi,
    SUM(CASE WHEN LOWER(IFNULL([{tipKolon}], '')) LIKE '%cikis%' THEN 1 ELSE 0 END) AS CikisSayisi
FROM [StokHareketler]
WHERE {filtre}";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return $"{baslik} stok hareket özeti:\nToplam Hareket: {reader["ToplamHareket"]}\nGiriş Sayısı: {reader["GirisSayisi"]}\nÇıkış Sayısı: {reader["CikisSayisi"]}";
            }

            return "";
        }

        private async Task<List<string>> KritikStoklarListeAsync(int adet)
        {
            var sonuc = new List<string>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            var hareketColumns = await GetTableColumnsAsync("StokHareketler");
            var urunColumns = await GetTableColumnsAsync("StokUrunler");

            if (hareketColumns.Count == 0 || urunColumns.Count == 0)
                return sonuc;

            string urunIdKolon = FindFirstExisting(hareketColumns, "StokUrunId", "UrunId");
            string tipKolon = FindFirstExisting(hareketColumns, "HareketTipi", "Tip");
            string miktarKolon = FindFirstExisting(hareketColumns, "Miktar", "Adet");
            string urunAdKolon = FindFirstExisting(urunColumns, "Ad", "UrunAdi", "StokAdi");

            if (string.IsNullOrWhiteSpace(urunIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(miktarKolon) ||
                string.IsNullOrWhiteSpace(urunAdKolon))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT u.[{urunAdKolon}] AS UrunAdi,
       IFNULL(SUM(
            CASE
                WHEN LOWER(IFNULL(h.[{tipKolon}], '')) LIKE '%giris%' THEN CAST(IFNULL(h.[{miktarKolon}], 0) AS REAL)
                WHEN LOWER(IFNULL(h.[{tipKolon}], '')) LIKE '%cikis%' THEN -CAST(IFNULL(h.[{miktarKolon}], 0) AS REAL)
                ELSE 0
            END
       ), 0) AS KalanMiktar
FROM [StokHareketler] h
LEFT JOIN [StokUrunler] u ON u.[Id] = h.[{urunIdKolon}]
GROUP BY u.[{urunAdKolon}]
HAVING KalanMiktar <= 5
ORDER BY KalanMiktar ASC
LIMIT {adet}";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sonuc.Add($"{reader["UrunAdi"]} | Kalan Tahmini: {reader["KalanMiktar"]}");
            }

            return sonuc;
        }

        private async Task<List<string>> YuksekKasaCikislariListeAsync(int adet)
        {
            var sonuc = new List<string>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            var columns = await GetTableColumnsAsync("KasaHareketler");
            if (columns.Count == 0)
                return sonuc;

            string tipKolon = FindFirstExisting(columns, "HareketTipi", "Tip");
            string tutarKolon = FindFirstExisting(columns, "Tutar", "Miktar");
            string tarihKolon = FindFirstExisting(columns, "Tarih", "IslemTarihi");
            string aciklamaKolon = FindFirstExisting(columns, "Aciklama", "Açıklama", "Not");

            if (string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tutarKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT [{tarihKolon}] AS Tarih,
       [{tutarKolon}] AS Tutar,
       {BuildSafeSelect("", aciklamaKolon, "Aciklama")}
FROM [KasaHareketler]
WHERE LOWER(IFNULL([{tipKolon}], '')) LIKE '%cikis%'
ORDER BY CAST(IFNULL([{tutarKolon}], 0) AS REAL) DESC
LIMIT {adet}";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tarih = reader["Tarih"]?.ToString() ?? "-";
                var tutar = reader["Tutar"]?.ToString() ?? "-";
                var aciklama = reader["Aciklama"]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(aciklama))
                    sonuc.Add($"{tarih} | {tutar} | {aciklama}");
                else
                    sonuc.Add($"{tarih} | {tutar}");
            }

            return sonuc;
        }

        private async Task<string> EnCokAvansVerilenCalisanRawAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslar");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0 || calisanColumns.Count == 0)
                return "";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "HareketTipi", "Tip");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tutarKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
                return "";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       IFNULL(SUM(CAST(IFNULL(h.[{tutarKolon}], 0) AS REAL)), 0) AS ToplamTutar
FROM [CalisanAvanslar] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE LOWER(IFNULL(h.[{tipKolon}], '')) LIKE '%avans%'
GROUP BY c.[{calisanAdKolon}]
ORDER BY ToplamTutar DESC
LIMIT 1";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return $"En çok avans verilen çalışan: {reader["CalisanAdi"]} | Toplam: {reader["ToplamTutar"]}";
            }

            return "";
        }

        private string BuildSafeSelect(string alias, string kolon, string asName)
        {
            if (string.IsNullOrWhiteSpace(kolon))
                return $"'' AS {asName}";

            if (string.IsNullOrWhiteSpace(alias))
                return $"[{kolon}] AS {asName}";

            return $"{alias}.[{kolon}] AS {asName}";
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            return text.Trim().ToLowerInvariant()
                .Replace('ı', 'i')
                .Replace('ğ', 'g')
                .Replace('ü', 'u')
                .Replace('ş', 's')
                .Replace('ö', 'o')
                .Replace('ç', 'c');
        }
    }
}