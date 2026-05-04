using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Microsoft.AspNetCore.Http;
using MuhasebeTakip2.App.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".MuhasebeTakip2.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Default");

    if (string.IsNullOrWhiteSpace(cs))
        throw new Exception("ConnectionStrings:Default bulunamadı.");

    if (cs.Contains("Host=", StringComparison.OrdinalIgnoreCase) &&
        cs.Contains("Database=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(cs);
    }
    else
    {
        options.UseSqlite(cs);
    }
});

var app = builder.Build();

// Veritabanını migration ile güncelle
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();

    if (!db.Firmalar.Any())
    {
        db.Firmalar.Add(new Firma
        {
            FirmaAdi = "Benim Firmam",
            AktifMi = true
        });

        db.SaveChanges();
    }

    if (!db.Kullanicilar.Any())
    {
        var firma = db.Firmalar.First();

        db.Kullanicilar.Add(new Kullanici
        {
            KullaniciAdi = "admin",
            Sifre = "1234",
            FirmaId = firma.Id,
            Rol = "SuperAdmin"
        });

        db.SaveChanges();
    }
}

// Eski verileri sadece ilk kurulumda mevcut firmaya bağla
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var firma = db.Firmalar.FirstOrDefault();

    if (firma != null)
    {
        var baglanacakVeriVarMi =
            db.CariKartlar.Any(x => x.FirmaId == null) ||
            db.Calisanlar.Any(x => x.FirmaId == null) ||
            db.CalisanAvanslari.Any(x => x.FirmaId == null) ||
            db.KasaHareketleri.Any(x => x.FirmaId == null) ||
            db.Musteriler.Any(x => x.FirmaId == null) ||
            db.MusteriIsler.Any(x => x.FirmaId == null) ||
            db.MusteriMasraflar.Any(x => x.FirmaId == null) ||
            db.StokHareketleri.Any(x => x.FirmaId == null) ||
            db.StokUrunler.Any(x => x.FirmaId == null) ||
            db.Cekler.Any(x => x.FirmaId == null);

        if (baglanacakVeriVarMi)
        {
            foreach (var x in db.CariKartlar.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.Calisanlar.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.CalisanAvanslari.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.KasaHareketleri.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.Musteriler.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.MusteriIsler.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.MusteriMasraflar.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.StokHareketleri.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.StokUrunler.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            foreach (var x in db.Cekler.Where(x => x.FirmaId == null))
                x.FirmaId = firma.Id;

            db.SaveChanges();
        }
    }
}

// Admin kullanıcısını düzelt
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var admin = db.Kullanicilar
        .Include(x => x.Firma)
        .FirstOrDefault(x => x.KullaniciAdi == "admin");

    if (admin != null)
    {
        admin.Rol = "SuperAdmin";

        if (admin.Firma != null)
            admin.Firma.AktifMi = true;

        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Render'da HTTPS yönlendirme sorun çıkarabildiği için
// sadece local/development ortamında çalıştırıyoruz.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";

    // API istekleri için özel hata yakalama
    if (path.StartsWith("/api"))
    {
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json; charset=utf-8";

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Muhasebe API içinde hata oluştu.",
                error = ex.Message,
                detail = ex.InnerException?.Message,
                path = path
            });
        }

        return;
    }

    var izinliSayfalar =
        path == "/" ||
        path.StartsWith("/login") ||
        path.StartsWith("/register") ||
        path.StartsWith("/error") ||
        path.StartsWith("/css") ||
        path.StartsWith("/js") ||
        path.StartsWith("/lib") ||
        path.StartsWith("/images") ||
        path.StartsWith("/favicon");

    var firmaId = context.Session.GetInt32("FirmaId");

    if (!izinliSayfalar && firmaId == null)
    {
        context.Response.Redirect("/");
        return;
    }

    await next();
});

app.UseAuthorization();


app.MapPost("/api/ai/calisan-avans-toplam", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.CalisanAdi))
        {
            return Results.Json(new CalisanAvansToplamResponse { Success = false, Total = 0, Message = "Çalışan adı gerekli." });
        }

        int year = request.Year ?? DateTime.Now.Year;
        int month = request.Month ?? DateTime.Now.Month;

        var firmaId = await db.Firmalar.Where(x => x.AktifMi).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        var ad = request.CalisanAdi.ToLower();

        var calisanQuery = db.Calisanlar.AsQueryable();
        if (firmaId != null)
            calisanQuery = calisanQuery.Where(x => x.FirmaId == firmaId);

        var calisan = await calisanQuery.FirstOrDefaultAsync(x => x.AdSoyad.ToLower().Contains(ad) || x.Ad.ToLower().Contains(ad));

        if (calisan == null)
        {
            return Results.Json(new CalisanAvansToplamResponse { Success = false, Total = 0, Message = $"{request.CalisanAdi} isimli çalışan bulunamadı." });
        }

        var aktifQuery = db.CalisanAvanslari.Where(x =>
            x.CalisanId == calisan.Id &&
            x.Tip == CalisanHareketTipi.Avans &&
            !x.ArsivlendiMi &&
            x.Tarih.Year == year &&
            x.Tarih.Month == month);

        if (firmaId != null)
            aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

        var aktifToplam = await aktifQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var arsivQuery = db.CalisanMaasArsivleri.Where(x =>
            x.CalisanId == calisan.Id &&
            x.DonemBaslangic.Year == year &&
            x.DonemBaslangic.Month == month);

        if (firmaId != null)
            arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

        var arsivToplam = await arsivQuery.SumAsync(x => (decimal?)x.ToplamAvans) ?? 0;
        var toplam = aktifToplam + arsivToplam;

        var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
        var ayAdi = ayAdlari[month];

        var kaynak = arsivToplam > 0 && aktifToplam > 0
            ? "Aktif kayıtlar ve maaş arşivi birlikte hesaplandı."
            : arsivToplam > 0
                ? "Bu bilgi maaş arşivinden alındı."
                : "Bu bilgi aktif kayıtlardan alındı.";

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = toplam > 0
                ? $"{calisan.AdSoyad} {ayAdi} ayında toplam {toplam:N2} TL avans aldı.\n{kaynak}"
                : $"{calisan.AdSoyad} için {ayAdi} ayında avans kaydı bulunamadı."
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message, detail = ex.InnerException?.Message, stack = ex.StackTrace }, statusCode: 500);
    }
});


app.MapPost("/api/ai/toplam-gelir", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    try
    {
        var (baslangic, bitis, ayAdi) = GetDateRange(request);

        var toplam = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Giris &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = $"{ayAdi} toplam gelir: {toplam:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/ai/son-avans-verilen-kisi", async (AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetSonAvansVerilenKisiAsync(db);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/bugun-kasa-durumu", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetBugunKasaDurumuAsync(db, request.CalisanAdi);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/en-borclu-musteri", async (AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetEnBorcluMusteriAsync(db);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/en-alacakli-satici", async (AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetEnAlacakliSaticiAsync(db);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/toplam-musteri-tahsilati", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetToplamMusteriTahsilatiAsync(db, request.DateRange);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/toplam-satici-odemesi", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetToplamSaticiOdemesiAsync(db, request.DateRange);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message,
            stack = ex.StackTrace
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/toplam-gider", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    try
    {
        var (baslangic, bitis, ayAdi) = GetDateRange(request);

        var toplam = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Cikis &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = $"{ayAdi} toplam gider: {toplam:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/ai/kasa-bakiye", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    try
    {
        var (baslangic, bitis, ayAdi) = GetDateRange(request);

        var giris = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Giris &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var cikis = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Cikis &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var bakiye = giris - cikis;

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = bakiye,
            Message = $"{ayAdi} kasa bakiyesi: {bakiye:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/ai/son-kasa-hareketleri", async (AppDbContext db) =>
{
    try
    {
        var liste = await db.KasaHareketleri
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => new
            {
                x.Tarih,
                x.Tip,
                x.Tutar,
                x.Aciklama
            })
            .ToListAsync();

        if (!liste.Any())
        {
            return Results.Json(new
            {
                success = true,
                message = "Kasa hareketi bulunamadı."
            });
        }

        var metin = "Son 10 kasa hareketi:\n\n";

        int i = 1;
        foreach (var item in liste)
        {
            var tip = item.Tip == HareketTipi.Giris ? "Giriş" : "Çıkış";
            var aciklama = string.IsNullOrWhiteSpace(item.Aciklama) ? "" : $" - {item.Aciklama}";

            metin += $"{i}. {item.Tarih:dd.MM.yyyy} - {tip} - {item.Tutar:N2} TL{aciklama}\n";
            i++;
        }

        return Results.Json(new
        {
            success = true,
            message = metin
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            detail = ex.InnerException?.Message
        }, statusCode: 500);
    }
});

app.MapPost("/api/ai/musteri-borc", async (AppDbContext db, CalisanAvansToplamRequest req) =>
{
    var result = await AiApiHelpers.GetMusteriBorcAsync(db, req.CalisanAdi);
    return Results.Json(result);
});

app.MapPost("/api/ai/musteri-sayisi", async (AppDbContext db) =>
{
    try
    {
        var count = await db.Musteriler.CountAsync();

        return Results.Json(new
        {
            success = true,
            message = $"Toplam müşteri sayısı: {count}"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = "Müşteri sayısı alınırken hata oluştu.",
            error = ex.Message,
            detail = ex.InnerException?.Message
        });
    }
});

app.MapPost("/api/ai/calisan-sayisi", async (AppDbContext db) =>
{
    var count = await db.Calisanlar.CountAsync();
    return Results.Json(new { success = true, message = $"Toplam çalışan sayısı: {count}" });
});

app.MapPost("/api/ai/cari-sayisi", async (AppDbContext db) =>
{
    var count = await db.CariKartlar.CountAsync();
    return Results.Json(new { success = true, message = $"Toplam cari sayısı: {count}" });
});

app.MapPost("/api/ai/alici-sayisi", async (AppDbContext db) =>
{
    var count = await db.CariKartlar.Where(x => x.Tip == CariTip.Alici).CountAsync();
    return Results.Json(new { success = true, message = $"Toplam alıcı sayısı: {count}" });
});

app.MapPost("/api/ai/satici-sayisi", async (AppDbContext db) =>
{
    var count = await db.CariKartlar.Where(x => x.Tip == CariTip.Satici).CountAsync();
    return Results.Json(new { success = true, message = $"Toplam satıcı sayısı: {count}" });
});

app.MapPost("/api/ai/stok-sayisi", async (AppDbContext db) =>
{
    var count = await db.StokUrunler.CountAsync();
    return Results.Json(new { success = true, message = $"Toplam stok ürün sayısı: {count}" });
});

app.MapPost("/api/ai/bugun-kasa-islem-sayisi", async (AppDbContext db) =>
{
    var bugun = DateTime.UtcNow.Date;
    var yarin = bugun.AddDays(1);

    var toplam = await db.KasaHareketleri
        .CountAsync(x => x.Tarih >= bugun && x.Tarih < yarin);

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = $"Bugün yapılan kasa işlem sayısı: {toplam}"
    });
});

app.MapPost("/api/ai/biten-stoklar", async (AppDbContext db) =>
{
    try
    {
        var stoklar = await db.StokUrunler
            .Select(u => new
            {
                Urun = u.Ad,
                Miktar = db.StokHareketleri
                    .Where(h => h.StokUrunId == u.Id)
                    .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
            })
            .Where(x => x.Miktar <= 0)
            .ToListAsync();

        if (!stoklar.Any())
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = true,
                Message = "Stokta biten ürün bulunmuyor."
            });
        }

        var metin = "Stokta biten ürünler:\n\n";

        foreach (var item in stoklar)
            metin += $"- {item.Urun}\n";

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = stoklar.Count,
            Message = metin
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = "Biten stoklar alınırken hata oluştu.",
            error = ex.Message,
            detail = ex.InnerException?.Message
        });
    }
});

app.MapPost("/api/ai/en-cok-stokta-olan-urun", async (AppDbContext db) =>
{
    try
    {
        var urun = await db.StokUrunler
            .Select(u => new
            {
                Urun = u.Ad,
                Miktar = db.StokHareketleri
                    .Where(h => h.StokUrunId == u.Id)
                    .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
            })
            .OrderByDescending(x => x.Miktar)
            .FirstOrDefaultAsync();

        if (urun == null)
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = true,
                Message = "Stok ürünü bulunamadı."
            });
        }

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = urun.Miktar,
            Message = $"Stokta en çok bulunan ürün: {urun.Urun} - {urun.Miktar:N2}"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = "En çok stokta olan ürün alınırken hata oluştu.",
            error = ex.Message,
            detail = ex.InnerException?.Message
        });
    }
});

app.MapPost("/api/ai/genel-ozet", async (AppDbContext db) =>
{
    var musteriSayisi = await db.Musteriler.CountAsync();
    var calisanSayisi = await db.Calisanlar.CountAsync();
    var cariSayisi = await db.CariKartlar.CountAsync();
    var stokUrunSayisi = await db.StokUrunler.CountAsync();

    var giris = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var cikis = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var bakiye = giris - cikis;

    var mesaj =
        $"Genel durum:\n\n" +
        $"- Kasa bakiyesi: {bakiye:N2} TL\n" +
        $"- Müşteri sayısı: {musteriSayisi}\n" +
        $"- Çalışan sayısı: {calisanSayisi}\n" +
        $"- Cari sayısı: {cariSayisi}\n" +
        $"- Stok ürün sayısı: {stokUrunSayisi}";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = bakiye,
        Message = mesaj
    });
});
app.MapPost("/api/ai/calisan-puantaj", async (AppDbContext db, CalisanAvansToplamRequest req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.CalisanAdi))
        {
            return Results.Json(new { success = false, message = "Çalışan adı gerekli." });
        }

        var ad = req.CalisanAdi.ToLower();

        var calisan = await db.Calisanlar
            .FirstOrDefaultAsync(x => x.Ad.ToLower().Contains(ad) || x.AdSoyad.ToLower().Contains(ad));

        if (calisan == null)
        {
            return Results.Json(new { success = false, message = "Çalışan bulunamadı." });
        }

        var now = DateTime.UtcNow;
        var baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var bitis = baslangic.AddMonths(1);

        var kayitlar = await db.Set<CalisanPuantaj>()
        .Where(x => x.CalisanId == calisan.Id && x.Tarih >= baslangic && x.Tarih < bitis)
        .ToListAsync();

        var geldi = kayitlar.Count(x => x.Durum == PuantajDurum.Geldi);
        var gelmedi = kayitlar.Count(x => x.Durum == PuantajDurum.Gelmedi);
        var izinli = kayitlar.Count(x => x.Durum == PuantajDurum.Izinli);
        var yarim = kayitlar.Count(x => x.Durum == PuantajDurum.YarimGun);

        return Results.Json(new
        {
            success = true,
            message = $"{calisan.AdSoyad} bu ay:\n" +
                      $"Geldi: {geldi}\n" +
                      $"Gelmedi: {gelmedi}\n" +
                      $"İzinli: {izinli}\n" +
                      $"Yarım Gün: {yarim}"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = "Puantaj alınırken hata oluştu.",
            error = ex.Message
        });
    }
});
app.MapPost("/api/ai/kar-durumu", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var gelir = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var gider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var kar = gelir - gider;

    var mesaj = kar >= 0
        ? $"{ayAdi} kâr etmiş görünüyorsun. Gelir: {gelir:N2} TL, gider: {gider:N2} TL, kâr: {kar:N2} TL"
        : $"{ayAdi} zarar etmiş görünüyorsun. Gelir: {gelir:N2} TL, gider: {gider:N2} TL, zarar: {Math.Abs(kar):N2} TL";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = kar,
        Message = mesaj
    });
});

app.MapPost("/api/ai/aylik-karsilastirma", async (AppDbContext db) =>
{
    var now = DateTime.UtcNow;
    var buAyBaslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);    var buAyBitis = buAyBaslangic.AddMonths(1);

    var gecenAyBaslangic = buAyBaslangic.AddMonths(-1);
    var gecenAyBitis = buAyBaslangic;

    var buAyGelir = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= buAyBaslangic && x.Tarih < buAyBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var buAyGider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= buAyBaslangic && x.Tarih < buAyBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var gecenAyGelir = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= gecenAyBaslangic && x.Tarih < gecenAyBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var gecenAyGider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= gecenAyBaslangic && x.Tarih < gecenAyBitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var buAyKar = buAyGelir - buAyGider;
    var gecenAyKar = gecenAyGelir - gecenAyGider;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = buAyKar,
        Message =
            $"Geçen aya göre durum:\n\n" +
            $"Bu ay gelir: {buAyGelir:N2} TL\n" +
            $"Bu ay gider: {buAyGider:N2} TL\n" +
            $"Bu ay sonuç: {buAyKar:N2} TL\n\n" +
            $"Geçen ay gelir: {gecenAyGelir:N2} TL\n" +
            $"Geçen ay gider: {gecenAyGider:N2} TL\n" +
            $"Geçen ay sonuç: {gecenAyKar:N2} TL"
    });
});

app.MapPost("/api/ai/en-cok-gider", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var gider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .GroupBy(x => string.IsNullOrWhiteSpace(x.Aciklama) ? "Açıklamasız gider" : x.Aciklama)
        .Select(g => new
        {
            Aciklama = g.Key,
            Toplam = g.Sum(x => x.Tutar)
        })
        .OrderByDescending(x => x.Toplam)
        .FirstOrDefaultAsync();

    if (gider == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Message = $"{ayAdi} gider kaydı bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = gider.Toplam,
        Message = $"{ayAdi} en çok gider: {gider.Aciklama} - {gider.Toplam:N2} TL"
    });
});

app.MapPost("/api/ai/en-cok-kazandiran-musteri", async (AppDbContext db) =>
{
    var musteri = await db.MusteriIsler
        .Include(x => x.Musteri)
        .GroupBy(x => new
        {
            x.MusteriId,
            MusteriAdi = x.Musteri != null ? x.Musteri.AdSoyad : "Bilinmeyen müşteri"
        })
        .Select(g => new
        {
            Musteri = g.Key.MusteriAdi,
            Toplam = g.Sum(x => x.Gelir)
        })
        .OrderByDescending(x => x.Toplam)
        .FirstOrDefaultAsync();

    if (musteri == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Message = "Müşteri geliri bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = musteri.Toplam,
        Message = $"En çok kazandıran müşteri: {musteri.Musteri} - {musteri.Toplam:N2} TL"
    });
});

app.MapPost("/api/ai/stok-durumu", async (AppDbContext db) =>
{
    var urunSayisi = await db.StokUrunler.CountAsync();

    var bitenStokSayisi = await db.StokUrunler
        .Select(u => new
        {
            Miktar = db.StokHareketleri
                .Where(h => h.StokUrunId == u.Id)
                .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
        })
        .CountAsync(x => x.Miktar <= 0);

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = urunSayisi,
        Message =
            $"Stok durumu:\n\n" +
            $"Toplam ürün sayısı: {urunSayisi}\n" +
            $"Biten stok sayısı: {bitenStokSayisi}"
    });
});



app.MapPost("/api/ai/maas-odeme-kontrol", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari.Where(x =>
        x.Tip == CalisanHareketTipi.MaasOdeme &&
        !x.ArsivlendiMi &&
        x.Tarih.Year == year &&
        x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifToplam = await aktifQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var arsivQuery = db.CalisanMaasArsivleri.Where(x =>
        x.DonemBaslangic < end &&
        x.DonemBitis >= start);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivToplam = await arsivQuery.SumAsync(x => (decimal?)x.ToplamMaas) ?? 0;
    var toplam = aktifToplam + arsivToplam;

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var kaynak = arsivToplam > 0 && aktifToplam > 0
        ? "Aktif kayıtlar ve maaş arşivi birlikte hesaplandı."
        : arsivToplam > 0
            ? "Bu bilgi maaş arşivinden alındı."
            : "Bu bilgi aktif kayıtlardan alındı.";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = toplam > 0
            ? $"{ayAdi} ayında maaş ödemesi yapılmış. Toplam maaş ödemesi: {toplam:N2} TL\n{kaynak}"
            : $"{ayAdi} ayında maaş ödemesi kaydı bulunamadı."
    });
});



app.MapPost("/api/ai/maas-odeme-dagilim", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x => x.Tip == CalisanHareketTipi.MaasOdeme &&
                    !x.ArsivlendiMi &&
                    x.Tarih.Year == year &&
                    x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifListe = await aktifQuery
        .GroupBy(x => x.Calisan != null ? x.Calisan.AdSoyad : x.Ad)
        .Select(g => new { Calisan = g.Key, Toplam = g.Sum(x => x.Tutar) })
        .ToListAsync();

   var arsivQuery = db.CalisanMaasArsivleri
    .Where(x =>
        x.DonemBaslangic < end &&
        x.DonemBitis >= start);
    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);
var arsivListe = await arsivQuery
    .Join(
        db.Calisanlar,
        arsiv => arsiv.CalisanId,
        calisan => calisan.Id,
        (arsiv, calisan) => new
        {
            Calisan = calisan.AdSoyad,
            Toplam = arsiv.ToplamMaas
        })
    .ToListAsync();

    var liste = aktifListe
        .Concat(arsivListe)
        .GroupBy(x => x.Calisan)
        .Select(g => new { Calisan = g.Key, Toplam = g.Sum(x => x.Toplam) })
        .Where(x => x.Toplam > 0)
        .OrderByDescending(x => x.Toplam)
        .ToList();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    if (!liste.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında maaş ödemesi kaydı bulunamadı."
        });
    }

    var mesaj = $"{ayAdi} ayında çalışanlara yapılan maaş ödemeleri:\n\n";

    foreach (var item in liste)
        mesaj += $"- {item.Calisan}: {item.Toplam:N2} TL\n";

    var aktifToplam = aktifListe.Sum(x => x.Toplam);
    var arsivToplam = arsivListe.Sum(x => x.Toplam);

    if (aktifToplam > 0 && arsivToplam > 0)
        mesaj += "\nAktif kayıtlar ve maaş arşivi birlikte hesaplandı.";
    else if (arsivToplam > 0)
        mesaj += "\nBu bilgi maaş arşivinden alındı.";
    else
        mesaj += "\nBu bilgi aktif kayıtlardan alındı.";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = liste.Sum(x => x.Toplam),
        Message = mesaj
    });
});

app.MapPost("/api/ai/maas-odeme-tarihleri", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var liste = await db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x => x.Tip == CalisanHareketTipi.MaasOdeme &&
                    x.Tarih.Year == year &&
                    x.Tarih.Month == month)
        .OrderByDescending(x => x.Tarih)
        .ThenByDescending(x => x.Id)
        .Select(x => new
        {
            Tarih = x.Tarih,
            Calisan = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            Tutar = x.Tutar
        })
        .ToListAsync();

    if (!liste.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında maaş ödeme tarihi bulunamadı."
        });
    }

    var mesaj = $"{ayAdi} ayında maaş ödeme tarihleri:\n\n";

    foreach (var item in liste)
        mesaj += $"- {item.Tarih:dd.MM.yyyy}: {item.Calisan} - {item.Tutar:N2} TL\n";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = liste.Sum(x => x.Tutar),
        Message = mesaj
    });
});

static (DateTime baslangic, DateTime bitis, string ayAdi) GetDateRange(CalisanAvansApiRequest request)
{
    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

    var now = DateTime.Now;

    if (request.Year.HasValue && request.Month.HasValue)
    {
        var baslangic = new DateTime(request.Year.Value, request.Month.Value, 1);
        return (baslangic, baslangic.AddMonths(1), $"{ayAdlari[request.Month.Value]} {request.Year.Value}");
    }

    if (request.DateRange == "LastMonth")
    {
        var baslangic = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        return (baslangic, baslangic.AddMonths(1), $"{ayAdlari[baslangic.Month]} {baslangic.Year}");
    }

    if (request.DateRange == "Today")
    {
        var baslangic = now.Date;
        return (baslangic, baslangic.AddDays(1), "Bugün");
    }

    var thisMonth = new DateTime(now.Year, now.Month, 1);
    return (thisMonth, thisMonth.AddMonths(1), $"{ayAdlari[thisMonth.Month]} {thisMonth.Year}");
}


app.MapPost("/api/ai/toplam-avans", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var firmaId = await db.Firmalar.Where(x => x.AktifMi).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari.Where(x =>
        x.Tip == CalisanHareketTipi.Avans &&
        !x.ArsivlendiMi &&
        x.Tarih.Year == year &&
        x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifToplam = await aktifQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var arsivQuery = db.CalisanMaasArsivleri.Where(x =>
        x.DonemBaslangic.Year == year &&
        x.DonemBaslangic.Month == month);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivToplam = await arsivQuery.SumAsync(x => (decimal?)x.ToplamAvans) ?? 0;
    var toplam = aktifToplam + arsivToplam;

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var kaynak = arsivToplam > 0 && aktifToplam > 0
        ? "Aktif kayıtlar ve maaş arşivi birlikte hesaplandı."
        : arsivToplam > 0
            ? "Bu bilgi maaş arşivinden alındı."
            : "Bu bilgi aktif kayıtlardan alındı.";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = toplam > 0
            ? $"{ayAdi} ayında verilen toplam avans: {toplam:N2} TL\n{kaynak}"
            : $"{ayAdi} ayında avans kaydı bulunamadı."
    });
});



app.MapPost("/api/ai/avans-dagilim", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var firmaId = await db.Firmalar.Where(x => x.AktifMi).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x => x.Tip == CalisanHareketTipi.Avans &&
                    !x.ArsivlendiMi &&
                    x.Tarih.Year == year &&
                    x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifListe = await aktifQuery
        .GroupBy(x => x.Calisan != null ? x.Calisan.AdSoyad : x.Ad)
        .Select(g => new { Kisi = g.Key, Toplam = g.Sum(x => x.Tutar) })
        .ToListAsync();

    var arsivQuery =
        from arsiv in db.CalisanMaasArsivleri
        join calisan in db.Calisanlar on arsiv.CalisanId equals calisan.Id
        where arsiv.DonemBaslangic.Year == year && arsiv.DonemBaslangic.Month == month
        select new { Kisi = calisan.AdSoyad, Toplam = arsiv.ToplamAvans, FirmaId = arsiv.FirmaId };

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivListe = await arsivQuery.ToListAsync();

    var liste = aktifListe
        .Concat(arsivListe.Select(x => new { x.Kisi, x.Toplam }))
        .GroupBy(x => x.Kisi)
        .Select(g => new { Kisi = g.Key, Toplam = g.Sum(x => x.Toplam) })
        .Where(x => x.Toplam > 0)
        .OrderByDescending(x => x.Toplam)
        .ToList();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    if (!liste.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse { Success = true, Total = 0, Message = $"{ayAdi} ayında avans kaydı bulunamadı." });
    }

    var mesaj = $"{ayAdi} ayında avans verilen çalışanlar:\n\n";
    foreach (var item in liste)
        mesaj += $"- {item.Kisi}: {item.Toplam:N2} TL\n";

    var aktifToplam = aktifListe.Sum(x => x.Toplam);
    var arsivToplam = arsivListe.Sum(x => x.Toplam);

    if (aktifToplam > 0 && arsivToplam > 0)
        mesaj += "\nAktif kayıtlar ve maaş arşivi birlikte hesaplandı.";
    else if (arsivToplam > 0)
        mesaj += "\nBu bilgi maaş arşivinden alındı.";
    else
        mesaj += "\nBu bilgi aktif kayıtlardan alındı.";

    return Results.Json(new CalisanAvansToplamResponse { Success = true, Total = liste.Sum(x => x.Toplam), Message = mesaj });
});



app.MapPost("/api/ai/en-cok-avans-alan", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var firmaId = await db.Firmalar.Where(x => x.AktifMi).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x => x.Tip == CalisanHareketTipi.Avans &&
                    !x.ArsivlendiMi &&
                    x.Tarih.Year == year &&
                    x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifListe = await aktifQuery
        .GroupBy(x => x.Calisan != null ? x.Calisan.AdSoyad : x.Ad)
        .Select(g => new { Kisi = g.Key, Toplam = g.Sum(x => x.Tutar) })
        .ToListAsync();

    var arsivQuery =
        from arsiv in db.CalisanMaasArsivleri
        join calisan in db.Calisanlar on arsiv.CalisanId equals calisan.Id
        where arsiv.DonemBaslangic.Year == year && arsiv.DonemBaslangic.Month == month
        select new { Kisi = calisan.AdSoyad, Toplam = arsiv.ToplamAvans, FirmaId = arsiv.FirmaId };

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivListe = await arsivQuery.ToListAsync();

    var kisi = aktifListe
        .Concat(arsivListe.Select(x => new { x.Kisi, x.Toplam }))
        .GroupBy(x => x.Kisi)
        .Select(g => new { Kisi = g.Key, Toplam = g.Sum(x => x.Toplam) })
        .Where(x => x.Toplam > 0)
        .OrderByDescending(x => x.Toplam)
        .FirstOrDefault();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    if (kisi == null)
    {
        return Results.Json(new CalisanAvansToplamResponse { Success = true, Total = 0, Message = $"{ayAdi} ayında avans kaydı bulunamadı." });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = kisi.Toplam,
        Message = $"{ayAdi} ayında en çok avans alan çalışan: {kisi.Kisi} - {kisi.Toplam:N2} TL"
    });
});


app.MapPost("/api/ai/son-avans", async (AppDbContext db) =>
{
    var son = await db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x => x.Tip == CalisanHareketTipi.Avans)
        .OrderByDescending(x => x.Tarih)
        .ThenByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    if (son == null)
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = "Hiç avans kaydı yok."
        });

    var ad = son.Calisan != null ? son.Calisan.AdSoyad : son.Ad;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = son.Tutar,
        Message = $"En son avans verilen kişi: {ad} - {son.Tutar:N2} TL ({son.Tarih:dd.MM.yyyy})"
    });
});


app.MapPost("/api/ai/calisan-maas-toplam", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.CalisanAdi))
    {
        return Results.Json(new CalisanAvansToplamResponse { Success = false, Total = 0, Message = "Çalışan adı gerekli." });
    }

    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var firmaId = await db.Firmalar.Where(x => x.AktifMi).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
    var ad = request.CalisanAdi.ToLower();

    var calisanQuery = db.Calisanlar.AsQueryable();
    if (firmaId != null)
        calisanQuery = calisanQuery.Where(x => x.FirmaId == firmaId);

    var calisan = await calisanQuery.FirstOrDefaultAsync(x => x.AdSoyad.ToLower().Contains(ad) || x.Ad.ToLower().Contains(ad));

    if (calisan == null)
    {
        return Results.Json(new CalisanAvansToplamResponse { Success = false, Total = 0, Message = $"{request.CalisanAdi} isimli çalışan bulunamadı." });
    }

    var aktifQuery = db.CalisanAvanslari.Where(x =>
        x.CalisanId == calisan.Id &&
        x.Tip == CalisanHareketTipi.MaasOdeme &&
        !x.ArsivlendiMi &&
        x.Tarih.Year == year &&
        x.Tarih.Month == month);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifToplam = await aktifQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var arsivQuery = db.CalisanMaasArsivleri.Where(x =>
        x.CalisanId == calisan.Id &&
        x.DonemBaslangic.Year == year &&
        x.DonemBaslangic.Month == month);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivToplam = await arsivQuery.SumAsync(x => (decimal?)x.ToplamMaas) ?? 0;
    var toplam = aktifToplam + arsivToplam;

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var kaynak = arsivToplam > 0 && aktifToplam > 0
        ? "Aktif kayıtlar ve maaş arşivi birlikte hesaplandı."
        : arsivToplam > 0
            ? "Bu bilgi maaş arşivinden alındı."
            : "Bu bilgi aktif kayıtlardan alındı.";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = toplam > 0
            ? $"{calisan.AdSoyad} için {ayAdi} ayında ödenen maaş: {toplam:N2} TL\n{kaynak}"
            : $"{calisan.AdSoyad} için {ayAdi} ayında maaş kaydı bulunamadı."
    });
});

app.MapRazorPages();

app.Run();

public class CalisanAvansApiRequest
{
    public string CalisanAdi { get; set; } = "";
    public string DateRange { get; set; } = "ThisMonth";
    public int? Year { get; set; }
    public int? Month { get; set; }
}