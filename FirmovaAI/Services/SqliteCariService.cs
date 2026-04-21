using Microsoft.Data.Sqlite;

namespace FirmovaAI.Services
{
    public class SqliteCariService
    {
        private readonly string _connectionString;

        public SqliteCariService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("EskiMuhasebeDb") ?? "";
        }

        public async Task<List<string>> GetAllTablesAsync()
        {
            var sonuc = new List<string>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type='table'
AND name NOT LIKE 'sqlite_%'
ORDER BY name";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sonuc.Add(reader["name"]?.ToString() ?? "");
            }

            return sonuc;
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

        public async Task<bool> TableExistsAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return false;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", tableName);

            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }

        public async Task<List<Dictionary<string, object>>> SearchRowsAsync(
            string tableName,
            string searchText,
            int take = 10,
            params string[] preferredNameColumns)
        {
            var sonuc = new List<Dictionary<string, object>>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            var columns = await GetTableColumnsAsync(tableName);
            if (columns.Count == 0)
                return sonuc;

            string? nameColumn = FindFirstExisting(columns, preferredNameColumns);
            if (string.IsNullOrWhiteSpace(nameColumn))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT * 
FROM [{tableName}]
WHERE LOWER(IFNULL([{nameColumn}], '')) LIKE LOWER(@aranan)
ORDER BY [{nameColumn}]
LIMIT {take}";
            cmd.Parameters.AddWithValue("@aranan", $"%{searchText}%");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i);
                }

                sonuc.Add(row);
            }

            return sonuc;
        }

        public async Task<Dictionary<string, object>?> GetCariDetayAsync(string aranan)
        {
            var liste = await SearchRowsAsync(
                "CariKartlar",
                aranan,
                1,
                "Unvan", "AdSoyad", "FirmaAdi", "CariAdi", "Ad");

            return liste.FirstOrDefault();
        }

        public async Task<Dictionary<string, object>?> GetStokDetayAsync(string aranan)
        {
            var liste = await SearchRowsAsync(
                "StokUrunler",
                aranan,
                1,
                "Ad", "UrunAdi", "StokAdi", "Kod");

            return liste.FirstOrDefault();
        }

        public string FindFirstExisting(List<string> columns, params string[] adaylar)
        {
            foreach (var aday in adaylar)
            {
                var bulunan = columns.FirstOrDefault(x => x.Equals(aday, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(bulunan))
                    return bulunan;
            }

            return "";
        }

        public string FormatCariDetay(Dictionary<string, object> row, List<string> columns)
        {
            string adKolon = FindFirstExisting(columns, "Unvan", "AdSoyad", "FirmaAdi", "CariAdi", "Ad");
            string telefonKolon = FindFirstExisting(columns, "Telefon", "Telefon1", "Gsm", "CepTelefonu");
            string vergiNoKolon = FindFirstExisting(columns, "VergiNo", "VKN", "TcKimlikNo", "TCKN");
            string tipKolon = FindFirstExisting(columns, "Tip", "CariTip");
            string emailKolon = FindFirstExisting(columns, "Email", "Eposta", "Mail");
            string adresKolon = FindFirstExisting(columns, "Adres", "AcikAdres");

            var satirlar = new List<string>();

            if (!string.IsNullOrWhiteSpace(adKolon) && row.ContainsKey(adKolon))
                satirlar.Add($"Cari: {row[adKolon]}");

            if (!string.IsNullOrWhiteSpace(telefonKolon) && row.ContainsKey(telefonKolon) && !BosMu(row[telefonKolon]))
                satirlar.Add($"Telefon: {row[telefonKolon]}");

            if (!string.IsNullOrWhiteSpace(vergiNoKolon) && row.ContainsKey(vergiNoKolon) && !BosMu(row[vergiNoKolon]))
                satirlar.Add($"Vergi No / Kimlik No: {row[vergiNoKolon]}");

            if (!string.IsNullOrWhiteSpace(tipKolon) && row.ContainsKey(tipKolon) && !BosMu(row[tipKolon]))
                satirlar.Add($"Tip: {row[tipKolon]}");

            if (!string.IsNullOrWhiteSpace(emailKolon) && row.ContainsKey(emailKolon) && !BosMu(row[emailKolon]))
                satirlar.Add($"E-posta: {row[emailKolon]}");

            if (!string.IsNullOrWhiteSpace(adresKolon) && row.ContainsKey(adresKolon) && !BosMu(row[adresKolon]))
                satirlar.Add($"Adres: {row[adresKolon]}");

            if (satirlar.Count == 0)
                satirlar.Add("Cari bulundu ama gösterilecek tanınmış alan bulunamadı.");

            return string.Join("\n", satirlar);
        }

        public string FormatStokDetay(Dictionary<string, object> row, List<string> columns)
        {
            string adKolon = FindFirstExisting(columns, "Ad", "UrunAdi", "StokAdi");
            string kodKolon = FindFirstExisting(columns, "Kod", "StokKodu", "UrunKodu");
            string birimKolon = FindFirstExisting(columns, "Birim", "OlcuBirimi");
            string barkodKolon = FindFirstExisting(columns, "Barkod");
            string aciklamaKolon = FindFirstExisting(columns, "Aciklama", "Açıklama");

            var satirlar = new List<string>();

            if (!string.IsNullOrWhiteSpace(adKolon) && row.ContainsKey(adKolon))
                satirlar.Add($"Ürün: {row[adKolon]}");

            if (!string.IsNullOrWhiteSpace(kodKolon) && row.ContainsKey(kodKolon) && !BosMu(row[kodKolon]))
                satirlar.Add($"Kod: {row[kodKolon]}");

            if (!string.IsNullOrWhiteSpace(birimKolon) && row.ContainsKey(birimKolon) && !BosMu(row[birimKolon]))
                satirlar.Add($"Birim: {row[birimKolon]}");

            if (!string.IsNullOrWhiteSpace(barkodKolon) && row.ContainsKey(barkodKolon) && !BosMu(row[barkodKolon]))
                satirlar.Add($"Barkod: {row[barkodKolon]}");

            if (!string.IsNullOrWhiteSpace(aciklamaKolon) && row.ContainsKey(aciklamaKolon) && !BosMu(row[aciklamaKolon]))
                satirlar.Add($"Açıklama: {row[aciklamaKolon]}");

            if (satirlar.Count == 0)
                satirlar.Add("Ürün bulundu ama gösterilecek tanınmış alan bulunamadı.");

            return string.Join("\n", satirlar);
        }

        private bool BosMu(object value)
        {
            return value == null || string.IsNullOrWhiteSpace(value.ToString());
        }
    }
}