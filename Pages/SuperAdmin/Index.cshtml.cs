using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;
using System.IO;
using System.Threading;

namespace MuhasebeTakip2.App.Pages.SuperAdmin;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Firma> Firmalar { get; set; } = new();

    [BindProperty]
    public string YeniFirmaAdi { get; set; } = "";

    [BindProperty]
    public string YeniKullaniciAdi { get; set; } = "";

    [BindProperty]
    public string YeniSifre { get; set; } = "";

    [BindProperty]
    public IFormFile? YedekDosya { get; set; }

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    private bool AdminYetkisiVarMi()
    {
        var rol = HttpContext.Session.GetString("Rol");
        return rol == "admin" || rol == "SuperAdmin";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!AdminYetkisiVarMi())
            return RedirectToPage("/Index");

        await YukleFirmalar();
        return Page();
    }

    public async Task<IActionResult> OnPostFirmaEkleAsync()
    {
        if (!AdminYetkisiVarMi())
            return RedirectToPage("/Index");

        YeniFirmaAdi = (YeniFirmaAdi ?? "").Trim();
        YeniKullaniciAdi = (YeniKullaniciAdi ?? "").Trim();
        YeniSifre = (YeniSifre ?? "").Trim();

        if (string.IsNullOrWhiteSpace(YeniFirmaAdi) ||
            string.IsNullOrWhiteSpace(YeniKullaniciAdi) ||
            string.IsNullOrWhiteSpace(YeniSifre))
        {
            Hata = "Tüm alanları doldurun.";
            await YukleFirmalar();
            return Page();
        }

        var kullaniciVar = await _db.Kullanicilar.AnyAsync(x => x.KullaniciAdi == YeniKullaniciAdi);
        if (kullaniciVar)
        {
            Hata = "Bu kullanıcı adı zaten kullanılıyor.";
            await YukleFirmalar();
            return Page();
        }

        var firma = new Firma
        {
            FirmaAdi = YeniFirmaAdi,
            AktifMi = true
        };

        _db.Firmalar.Add(firma);
        await _db.SaveChangesAsync();

        var kullanici = new Kullanici
        {
            KullaniciAdi = YeniKullaniciAdi,
            Sifre = PasswordHelper.Hash(YeniSifre),
            FirmaId = firma.Id,
            Rol = "Kullanici"
        };

        _db.Kullanicilar.Add(kullanici);
        await _db.SaveChangesAsync();

        Mesaj = "Yeni firma ve kullanıcı oluşturuldu.";

        YeniFirmaAdi = "";
        YeniKullaniciAdi = "";
        YeniSifre = "";

        await YukleFirmalar();
        return Page();
    }

    public async Task<IActionResult> OnPostDurumDegistirAsync(int firmaId)
    {
        if (!AdminYetkisiVarMi())
            return RedirectToPage("/Index");

        var firma = await _db.Firmalar.FirstOrDefaultAsync(x => x.Id == firmaId);

        if (firma == null)
        {
            Hata = "Firma bulunamadı.";
            await YukleFirmalar();
            return Page();
        }

        firma.AktifMi = !firma.AktifMi;
        await _db.SaveChangesAsync();

        Mesaj = $"{firma.FirmaAdi} için durum güncellendi.";

        await YukleFirmalar();
        return Page();
    }

    public IActionResult OnPostYedekAl()
    {
        if (!AdminYetkisiVarMi())
            return RedirectToPage("/Index");

        try
        {
            var kaynakDbPath = VeritabaniYolunuBul();

            if (!System.IO.File.Exists(kaynakDbPath))
            {
                Hata = "Veritabanı bulunamadı.";
                return Page();
            }

            var tempBackupPath = Path.Combine(
                Path.GetTempPath(),
                $"yedek_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.db");

            var sourceConnectionString = _db.Database.GetDbConnection().ConnectionString;
            var destinationConnectionString = $"Data Source={tempBackupPath}";

            using (var source = new SqliteConnection(sourceConnectionString))
            using (var destination = new SqliteConnection(destinationConnectionString))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            byte[] bytes = Array.Empty<byte>();
            Exception? lastError = null;

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var fs = new FileStream(
                        tempBackupPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);

                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    bytes = ms.ToArray();
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(200);
                }
            }

            if (lastError != null)
            {
                Hata = $"Yedek alınamadı: {lastError.Message}";
                return Page();
            }

            var fileName = $"yedek_{DateTime.Now:yyyyMMdd_HHmmss}.db";

            try
            {
                System.IO.File.Delete(tempBackupPath);
            }
            catch
            {
            }

            return File(bytes, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            Hata = $"Yedek alınamadı: {ex.Message}";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostYedekYukleAsync()
    {
        if (!AdminYetkisiVarMi())
            return RedirectToPage("/Index");

        if (YedekDosya == null || YedekDosya.Length == 0)
        {
            Hata = "Lütfen bir yedek dosyası seçin.";
            await YukleFirmalar();
            return Page();
        }

        var ext = Path.GetExtension(YedekDosya.FileName).ToLower();
        if (ext != ".db")
        {
            Hata = "Sadece .db dosyası yükleyebilirsiniz.";
            await YukleFirmalar();
            return Page();
        }

        var dbPath = VeritabaniYolunuBul();

        if (!System.IO.File.Exists(dbPath))
        {
            Hata = "Mevcut veritabanı bulunamadı.";
            await YukleFirmalar();
            return Page();
        }

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await YedekDosya.CopyToAsync(stream);
            }

            _db.ChangeTracker.Clear();
            _db.Database.CloseConnection();
            SqliteConnection.ClearAllPools();

            var backupPath = dbPath + ".backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            System.IO.File.Copy(dbPath, backupPath, true);

            System.IO.File.Delete(dbPath);
            System.IO.File.Copy(tempPath, dbPath);

            Mesaj = "Yedek başarıyla geri yüklendi. Sayfayı yenileyin.";
        }
        catch (Exception ex)
        {
            Hata = "Geri yükleme hatası: " + ex.Message;
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
            catch
            {
            }
        }

        await YukleFirmalar();
        return Page();
    }

    private async Task YukleFirmalar()
    {
        Firmalar = await _db.Firmalar
            .OrderBy(x => x.FirmaAdi)
            .ToListAsync();
    }

    private string VeritabaniYolunuBul()
    {
        var connectionString = _db.Database.GetDbConnection().ConnectionString;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dbPath = builder.DataSource;

        if (!Path.IsPathRooted(dbPath))
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbPath);

        return dbPath;
    }
}