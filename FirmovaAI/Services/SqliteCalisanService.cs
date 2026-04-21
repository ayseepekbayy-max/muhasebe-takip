using Microsoft.Data.Sqlite;

namespace FirmovaAI.Services
{
    public class SqliteCalisanService
    {
        private readonly string _connectionString;

        public SqliteCalisanService(IConfiguration configuration)
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

        public async Task<List<Dictionary<string, object>>> SearchCalisanAsync(string aranan, int take = 10)
        {
            var sonuc = new List<Dictionary<string, object>>();

            if (string.IsNullOrWhiteSpace(_connectionString))
                return sonuc;

            var columns = await GetTableColumnsAsync("Calisanlar");
            if (columns.Count == 0)
                return sonuc;

            string adKolon = FindFirstExisting(columns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");
            if (string.IsNullOrWhiteSpace(adKolon))
                return sonuc;

            using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync();

            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT * 
FROM [Calisanlar]
WHERE LOWER(IFNULL([{adKolon}], '')) LIKE LOWER(@aranan)
ORDER BY [{adKolon}]
LIMIT {take}";
            cmd.Parameters.AddWithValue("@aranan", $"%{aranan}%");

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

        public async Task<Dictionary<string, object>?> GetCalisanDetayAsync(string aranan)
        {
            var liste = await SearchCalisanAsync(aranan, 1);
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

        public string FormatCalisanDetay(Dictionary<string, object> row, List<string> columns)
        {
            string adKolon = FindFirstExisting(columns, "AdSoyad", "Ad", "Isim", "AdiSoyadi", "Adi");
            string telefonKolon = FindFirstExisting(columns, "Telefon", "Telefon1", "CepTelefonu", "Gsm", "Cep");
            string maasKolon = FindFirstExisting(columns, "Maas", "Maaş", "AylikMaas", "Ucret", "Ucreti");
            string iseGirisKolon = FindFirstExisting(columns, "IseGirisTarihi", "İseGirisTarihi", "GirisTarihi", "IseBaslamaTarihi");
            string aciklamaKolon = FindFirstExisting(columns, "Aciklama", "Açıklama", "Not", "Notlar");

            var satirlar = new List<string>();

            if (!string.IsNullOrWhiteSpace(adKolon) && row.ContainsKey(adKolon))
                satirlar.Add($"Çalışan: {row[adKolon]}");

            if (!string.IsNullOrWhiteSpace(telefonKolon) && row.ContainsKey(telefonKolon) && !BosMu(row[telefonKolon]))
                satirlar.Add($"Telefon: {row[telefonKolon]}");

            if (!string.IsNullOrWhiteSpace(maasKolon) && row.ContainsKey(maasKolon) && !BosMu(row[maasKolon]))
                satirlar.Add($"Maaş: {row[maasKolon]}");

            if (!string.IsNullOrWhiteSpace(iseGirisKolon) && row.ContainsKey(iseGirisKolon) && !BosMu(row[iseGirisKolon]))
                satirlar.Add($"İşe Giriş Tarihi: {row[iseGirisKolon]}");

            if (!string.IsNullOrWhiteSpace(aciklamaKolon) && row.ContainsKey(aciklamaKolon) && !BosMu(row[aciklamaKolon]))
                satirlar.Add($"Açıklama: {row[aciklamaKolon]}");

            if (satirlar.Count == 0)
                satirlar.Add("Çalışan bulundu ama gösterilecek tanınmış alan bulunamadı.");

            return string.Join("\n", satirlar);
        }

        private bool BosMu(object value)
        {
            return value == null || string.IsNullOrWhiteSpace(value.ToString());
        }
    }
}