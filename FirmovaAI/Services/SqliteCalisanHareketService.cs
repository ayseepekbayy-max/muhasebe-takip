using Microsoft.Data.Sqlite;

namespace FirmovaAI.Services
{
    public class SqliteCalisanHareketService
    {
        private readonly string _connectionString;

        public SqliteCalisanHareketService(IConfiguration configuration)
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

        public async Task<string> EnSonAvansVerilenKisiAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0)
                return "CalisanAvanslari tablosu bulunamadı.";

            if (calisanColumns.Count == 0)
                return "Calisanlar tablosu bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string aciklamaKolon = FindFirstExisting(hareketColumns, "Aciklama", "Açıklama", "Not");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "Gerekli çalışan hareket sütunları bulunamadı.";
            }

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       h.[{tarihKolon}] AS Tarih,
       {BuildSafeSelect("h", tutarKolon, "Tutar")},
       {BuildSafeSelect("h", aciklamaKolon, "Aciklama")}
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE {BuildTipFilterSql("h", tipKolon, "avans")}
  {BuildArchiveFilterSql("h", arsivKolon)}
ORDER BY h.[{tarihKolon}] DESC
LIMIT 1";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? "Bilinmeyen çalışan";
                string tarih = reader["Tarih"]?.ToString() ?? "-";
                string tutar = reader["Tutar"]?.ToString() ?? "-";
                string aciklama = reader["Aciklama"]?.ToString() ?? "";

                var sonuc = $"En son avans verilen kişi: {ad}\nTarih: {tarih}\nTutar: {tutar}";
                if (!string.IsNullOrWhiteSpace(aciklama))
                    sonuc += $"\nAçıklama: {aciklama}";

                return sonuc;
            }

            return "Avans kaydı bulunamadı.";
        }

        public async Task<string> KisiyeAitSonHareketAsync(string arananKisi, string hareketTipi)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            if (string.IsNullOrWhiteSpace(arananKisi))
                return "Çalışan adı boş görünüyor.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0)
                return "CalisanAvanslari tablosu bulunamadı.";

            if (calisanColumns.Count == 0)
                return "Calisanlar tablosu bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string aciklamaKolon = FindFirstExisting(hareketColumns, "Aciklama", "Açıklama", "Not");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "Gerekli çalışan hareket sütunları bulunamadı.";
            }

            string tipFiltre = NormalizeText(hareketTipi).Contains("maas") ? "maas" : "avans";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       h.[{tarihKolon}] AS Tarih,
       {BuildSafeSelect("h", tutarKolon, "Tutar")},
       {BuildSafeSelect("h", aciklamaKolon, "Aciklama")}
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE LOWER(IFNULL(c.[{calisanAdKolon}], '')) LIKE LOWER(@ad)
  AND {BuildTipFilterSql("h", tipKolon, tipFiltre)}
  {BuildArchiveFilterSql("h", arsivKolon)}
ORDER BY h.[{tarihKolon}] DESC
LIMIT 1";

            cmd.Parameters.AddWithValue("@ad", $"%{arananKisi}%");

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? arananKisi;
                string tarih = reader["Tarih"]?.ToString() ?? "-";
                string tutar = reader["Tutar"]?.ToString() ?? "-";
                string aciklama = reader["Aciklama"]?.ToString() ?? "";

                string baslik = tipFiltre == "maas" ? "maaş" : "avans";

                var sonuc = $"{ad} için en son {baslik} kaydı:\nTarih: {tarih}\nTutar: {tutar}";
                if (!string.IsNullOrWhiteSpace(aciklama))
                    sonuc += $"\nAçıklama: {aciklama}";

                return sonuc;
            }

            return tipFiltre == "maas"
                ? $"{arananKisi} için maaş kaydı bulunamadı."
                : $"{arananKisi} için avans kaydı bulunamadı.";
        }

        public async Task<string> KisiyeAitToplamAvansAsync(string arananKisi)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            if (string.IsNullOrWhiteSpace(arananKisi))
                return "Çalışan adı boş görünüyor.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0)
                return "CalisanAvanslari tablosu bulunamadı.";

            if (calisanColumns.Count == 0)
                return "Calisanlar tablosu bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tutarKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "Toplam avans hesabı için gerekli sütunlar bulunamadı.";
            }

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       COUNT(*) AS KayitSayisi,
       IFNULL(SUM(CAST(IFNULL(h.[{tutarKolon}], 0) AS REAL)), 0) AS ToplamTutar,
       MAX(h.[{tarihKolon}]) AS SonTarih
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE LOWER(IFNULL(c.[{calisanAdKolon}], '')) LIKE LOWER(@ad)
  AND {BuildTipFilterSql("h", tipKolon, "avans")}
  {BuildArchiveFilterSql("h", arsivKolon)}
GROUP BY c.[{calisanAdKolon}]
ORDER BY ToplamTutar DESC
LIMIT 1";

            cmd.Parameters.AddWithValue("@ad", $"%{arananKisi}%");

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? arananKisi;
                string kayitSayisi = reader["KayitSayisi"]?.ToString() ?? "0";
                string toplamTutar = reader["ToplamTutar"]?.ToString() ?? "0";
                string sonTarih = reader["SonTarih"]?.ToString() ?? "-";

                return $"{ad} için toplam avans bilgisi:\nKayıt Sayısı: {kayitSayisi}\nToplam Avans: {toplamTutar}\nSon Avans Tarihi: {sonTarih}";
            }

            return $"{arananKisi} için avans kaydı bulunamadı.";
        }

        public async Task<string> BuAyKimlereMaasOdendiAsync()
        {
            return await BuAyHareketListesiAsync("maas", "Bu ay maaş ödenen çalışanlar");
        }

        public async Task<string> BuAyKimlereAvansVerildiAsync()
        {
            return await BuAyHareketListesiAsync("avans", "Bu ay avans verilen çalışanlar");
        }

        public async Task<string> EnCokAvansVerilenCalisanAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0 || calisanColumns.Count == 0)
                return "Gerekli tablolar bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tutarKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "En çok avans sorgusu için gerekli sütunlar bulunamadı.";
            }

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       COUNT(*) AS KayitSayisi,
       IFNULL(SUM(CAST(IFNULL(h.[{tutarKolon}], 0) AS REAL)), 0) AS ToplamTutar,
       MAX(h.[{tarihKolon}]) AS SonTarih
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE {BuildTipFilterSql("h", tipKolon, "avans")}
  {BuildArchiveFilterSql("h", arsivKolon)}
GROUP BY c.[{calisanAdKolon}]
ORDER BY ToplamTutar DESC
LIMIT 1";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? "Bilinmeyen çalışan";
                string kayitSayisi = reader["KayitSayisi"]?.ToString() ?? "0";
                string toplamTutar = reader["ToplamTutar"]?.ToString() ?? "0";
                string sonTarih = reader["SonTarih"]?.ToString() ?? "-";

                return $"En çok avans verilen çalışan: {ad}\nToplam Avans: {toplamTutar}\nKayıt Sayısı: {kayitSayisi}\nSon Hareket Tarihi: {sonTarih}";
            }

            return "Avans kaydı bulunamadı.";
        }

        public async Task<string> ToplamMaasOdemesiAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            if (hareketColumns.Count == 0)
                return "CalisanAvanslari tablosu bulunamadı.";

            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");

            if (string.IsNullOrWhiteSpace(tipKolon) || string.IsNullOrWhiteSpace(tutarKolon))
                return "Toplam maaş ödemesi için gerekli sütunlar bulunamadı.";

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT COUNT(*) AS KayitSayisi,
       IFNULL(SUM(CAST(IFNULL([{tutarKolon}], 0) AS REAL)), 0) AS ToplamTutar,
       {BuildSafeAggregateMax(tarihKolon, "SonTarih")}
FROM [CalisanAvanslari]
WHERE {BuildTipFilterSql("", tipKolon, "maas")}
  {BuildArchiveFilterSql("", arsivKolon)}";

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string kayitSayisi = reader["KayitSayisi"]?.ToString() ?? "0";
                string toplamTutar = reader["ToplamTutar"]?.ToString() ?? "0";
                string sonTarih = reader["SonTarih"]?.ToString() ?? "-";

                return $"Toplam maaş ödemesi:\nKayıt Sayısı: {kayitSayisi}\nToplam Tutar: {toplamTutar}\nSon Maaş Tarihi: {sonTarih}";
            }

            return "Maaş kaydı bulunamadı.";
        }

        public async Task<string> SonCalisanHareketleriAsync(int adet = 5)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0 || calisanColumns.Count == 0)
                return "Gerekli tablolar bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
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
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
ORDER BY h.[{tarihKolon}] DESC
LIMIT {adet}";

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
                return "Çalışan hareket kaydı bulunamadı.";

            return "Son çalışan hareketleri:\n- " + string.Join("\n- ", satirlar);
        }

        private async Task<string> BuAyHareketListesiAsync(string tipFiltre, string baslik)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return "Veritabanı bağlantısı bulunamadı.";

            var hareketColumns = await GetTableColumnsAsync("CalisanAvanslari");
            var calisanColumns = await GetTableColumnsAsync("Calisanlar");

            if (hareketColumns.Count == 0 || calisanColumns.Count == 0)
                return "Gerekli tablolar bulunamadı.";

            string calisanIdKolon = FindFirstExisting(hareketColumns, "CalisanId");
            string tipKolon = FindFirstExisting(hareketColumns, "Tip", "HareketTipi");
            string tarihKolon = FindFirstExisting(hareketColumns, "Tarih", "IslemTarihi");
            string tutarKolon = FindFirstExisting(hareketColumns, "Tutar", "Miktar");
            string arsivKolon = FindFirstExisting(hareketColumns, "ArsivlendiMi");
            string calisanAdKolon = FindFirstExisting(calisanColumns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");

            if (string.IsNullOrWhiteSpace(calisanIdKolon) ||
                string.IsNullOrWhiteSpace(tipKolon) ||
                string.IsNullOrWhiteSpace(tarihKolon) ||
                string.IsNullOrWhiteSpace(calisanAdKolon))
            {
                return "Bu ay hareket listesi için gerekli sütunlar bulunamadı.";
            }

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT c.[{calisanAdKolon}] AS CalisanAdi,
       h.[{tarihKolon}] AS Tarih,
       {BuildSafeSelect("h", tutarKolon, "Tutar")}
FROM [CalisanAvanslari] h
LEFT JOIN [Calisanlar] c ON c.[Id] = h.[{calisanIdKolon}]
WHERE {BuildTipFilterSql("h", tipKolon, tipFiltre)}
  {BuildArchiveFilterSql("h", arsivKolon)}
  AND strftime('%Y-%m', h.[{tarihKolon}]) = strftime('%Y-%m', 'now', 'localtime')
ORDER BY h.[{tarihKolon}] DESC";

            using var reader = await cmd.ExecuteReaderAsync();
            var satirlar = new List<string>();

            while (await reader.ReadAsync())
            {
                string ad = reader["CalisanAdi"]?.ToString() ?? "Bilinmeyen çalışan";
                string tarih = reader["Tarih"]?.ToString() ?? "-";
                string tutar = reader["Tutar"]?.ToString() ?? "-";

                satirlar.Add($"{ad} | {tarih} | {tutar}");
            }

            if (satirlar.Count == 0)
                return $"{baslik} bulunamadı.";

            return $"{baslik}:\n- " + string.Join("\n- ", satirlar);
        }

        private string BuildTipFilterSql(string alias, string tipKolon, string tip)
        {
            string kolon = string.IsNullOrWhiteSpace(alias)
                ? $"[{tipKolon}]"
                : $"{alias}.[{tipKolon}]";

            string tipNormalize = NormalizeText(tip);

            if (tipNormalize.Contains("maas"))
            {
                return $@"(
    LOWER(CAST(IFNULL({kolon}, '') AS TEXT)) LIKE '%maas%'
    OR LOWER(CAST(IFNULL({kolon}, '') AS TEXT)) LIKE '%maasodeme%'
    OR {kolon} = 2
)";
            }

            return $@"(
    LOWER(CAST(IFNULL({kolon}, '') AS TEXT)) LIKE '%avans%'
    OR {kolon} = 1
)";
        }

        private string BuildArchiveFilterSql(string alias, string arsivKolon)
        {
            if (string.IsNullOrWhiteSpace(arsivKolon))
                return "";

            string kolon = string.IsNullOrWhiteSpace(alias)
                ? $"[{arsivKolon}]"
                : $"{alias}.[{arsivKolon}]";

            return $"AND IFNULL({kolon}, 0) = 0";
        }

        private string BuildSafeSelect(string alias, string kolon, string asName)
        {
            if (string.IsNullOrWhiteSpace(kolon))
                return $"'' AS {asName}";

            if (string.IsNullOrWhiteSpace(alias))
                return $"[{kolon}] AS {asName}";

            return $"{alias}.[{kolon}] AS {asName}";
        }

        private string BuildSafeAggregateMax(string kolon, string asName)
        {
            if (string.IsNullOrWhiteSpace(kolon))
                return $"'' AS {asName}";

            return $"MAX([{kolon}]) AS {asName}";
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