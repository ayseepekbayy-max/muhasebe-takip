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
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = false,
                Total = 0,
                Message = "Çalışan adı gerekli."
            });
        }

        int year = request.Year ?? DateTime.Now.Year;
        int month = request.Month ?? DateTime.Now.Month;

        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var firmaId = await db.Firmalar
            .Where(x => x.AktifMi)
            .OrderBy(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
        var ayAdi = ayAdlari[month];

        var ad = (request.CalisanAdi ?? "").Trim().ToLower();

        var calisanQuery = db.Calisanlar.AsQueryable();

        if (firmaId != null)
            calisanQuery = calisanQuery.Where(x => x.FirmaId == firmaId);

        var tumCalisanlar = await calisanQuery.ToListAsync();

        var calisan = tumCalisanlar.FirstOrDefault(x =>
        {
            var tamAd = (x.AdSoyad ?? "").Trim().ToLower();
            var kisaAd = (x.Ad ?? "").Trim().ToLower();

            return tamAd == ad
                || tamAd.StartsWith(ad + " ")
                || kisaAd == ad;
        });

        if (calisan == null)
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = false,
                Total = 0,
                Message = $"{request.CalisanAdi} isimli çalışan bulunamadı."
            });
        }

        var calisanTamAd = (calisan.AdSoyad ?? "").Trim().ToLower();
        var calisanKisaAd = (calisan.Ad ?? "").Trim().ToLower();

        var tumAvanslar = await db.CalisanAvanslari
            .Where(x =>
                x.Tip == CalisanHareketTipi.Avans &&
                x.Tarih >= start &&
                x.Tarih < end &&
                (firmaId == null || x.FirmaId == firmaId) &&
                (
                    x.CalisanId == calisan.Id ||
                    ((x.Ad ?? "").Trim().ToLower() == calisanTamAd) ||
                    ((x.Ad ?? "").Trim().ToLower() == calisanKisaAd)
                ))
            .OrderBy(x => x.Tarih)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.CalisanId,
                x.Tarih,
                x.Tutar,
                x.ArsivlendiMi
            })
            .ToListAsync();

        var maasArsivleri = await db.CalisanMaasArsivleri
            .Where(x =>
                x.CalisanId == calisan.Id &&
                (firmaId == null || x.FirmaId == firmaId))
            .Select(x => new
            {
                x.CalisanId,
                x.OdemeTarihi
            })
            .ToListAsync();

        var avanslar = tumAvanslar
            .Where(x =>
            {
                if (!x.ArsivlendiMi)
                    return true;

                var ayniAydaMaaslaKapanmisMi = maasArsivleri.Any(a =>
                    a.CalisanId == x.CalisanId &&
                    a.OdemeTarihi.Date >= x.Tarih.Date &&
                    a.OdemeTarihi.Year == x.Tarih.Year &&
                    a.OdemeTarihi.Month == x.Tarih.Month);

                return !ayniAydaMaaslaKapanmisMi;
            })
            .ToList();

        var toplam = avanslar.Sum(x => x.Tutar);

        if (!avanslar.Any())
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = true,
                Total = 0,
                Message = $"{calisan.AdSoyad} için {ayAdi} ayında avans kaydı bulunamadı."
            });
        }

        var mesaj = $"{calisan.AdSoyad} {ayAdi} ayı avansları ({start:dd.MM.yyyy} - {end.AddDays(-1):dd.MM.yyyy}):\n\n";

        foreach (var item in avanslar)
            mesaj += $"- {item.Tarih:dd.MM.yyyy}: {item.Tutar:N2} TL\n";

        mesaj += $"\nToplam: {toplam:N2} TL";

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = toplam,
            Message = mesaj
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
app.MapPost("/api/ai/calisan-puantaj", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    return await GetCalisanPuantajOzetiAsync(db, req);
});

app.MapPost("/api/ai/calisan-devamsizlik", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    return await GetCalisanPuantajOzetiAsync(db, req);
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

    string mesaj;

    if (gelir == 0 && gider == 0)
    {
        mesaj = $"{ayAdi} için gelir veya gider kaydı bulunamadı. Bu yüzden kâr/zarar yorumu yapılamıyor.";
    }
    else if (kar > 0)
    {
        mesaj = $"{ayAdi} kâr etmiş görünüyorsun. Gelir: {gelir:N2} TL, gider: {gider:N2} TL, kâr: {kar:N2} TL";
    }
    else if (kar < 0)
    {
        mesaj = $"{ayAdi} zarar etmiş görünüyorsun. Gelir: {gelir:N2} TL, gider: {gider:N2} TL, zarar: {Math.Abs(kar):N2} TL";
    }
    else
    {
        mesaj = $"{ayAdi} döneminde gelir ve gider birbirine eşit görünüyor. Gelir: {gelir:N2} TL, gider: {gider:N2} TL, net sonuç: 0.00 TL";
    }

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

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.Tip == CalisanHareketTipi.MaasOdeme &&
            !x.ArsivlendiMi &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifListe = await aktifQuery
        .Select(x => new
        {
            Kisi = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            x.Tutar
        })
        .ToListAsync();

    var aktifToplam = aktifListe
        .GroupBy(x => x.Kisi)
        .Select(g => g.Sum(x => x.Tutar))
        .Sum();

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x =>
            x.OdemeTarihi >= start &&
            x.OdemeTarihi < end);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivTumListe = await arsivQuery
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                arsiv.Id,
                Kisi = calisan.AdSoyad,
                arsiv.OdemeTarihi,
                arsiv.ToplamMaas
            })
        .ToListAsync();

    var arsivListe = arsivTumListe
        .GroupBy(x => x.Kisi)
        .Select(g => g.OrderByDescending(x => x.OdemeTarihi).ThenByDescending(x => x.Id).First())
        .ToList();

    var arsivToplam = arsivListe.Sum(x => x.ToplamMaas);
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

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var aktifQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.Tip == CalisanHareketTipi.MaasOdeme &&
            !x.ArsivlendiMi &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifListe = await aktifQuery
        .Select(x => new
        {
            Calisan = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            Toplam = x.Tutar
        })
        .ToListAsync();

    var aktifGruplu = aktifListe
        .GroupBy(x => x.Calisan)
        .Select(g => new { Calisan = g.Key, Toplam = g.Sum(x => x.Toplam) })
        .ToList();

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x =>
            x.OdemeTarihi >= start &&
            x.OdemeTarihi < end);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsivTumListe = await arsivQuery
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                arsiv.Id,
                Calisan = calisan.AdSoyad,
                arsiv.ToplamMaas,
                arsiv.OdemeTarihi
            })
        .ToListAsync();

    var arsivListe = arsivTumListe
        .GroupBy(x => x.Calisan)
        .Select(g => g.OrderByDescending(x => x.OdemeTarihi).ThenByDescending(x => x.Id).First())
        .Select(x => new
        {
            x.Calisan,
            Toplam = x.ToplamMaas
        })
        .ToList();

    var liste = aktifGruplu
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

    var aktifToplam = aktifGruplu.Sum(x => x.Toplam);
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

    var now = DateTime.UtcNow;

    if (request.Year.HasValue && request.Month.HasValue)
    {
        var baslangic = new DateTime(request.Year.Value, request.Month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
        return (baslangic, baslangic.AddMonths(1), $"{ayAdlari[request.Month.Value]} {request.Year.Value}");
    }

    if (request.DateRange == "LastMonth")
    {
        var baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        return (baslangic, baslangic.AddMonths(1), $"{ayAdlari[baslangic.Month]} {baslangic.Year}");
    }

    if (request.DateRange == "Today")
    {
        var baslangic = now.Date;
        return (baslangic, baslangic.AddDays(1), "Bugün");
    }

    var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    return (thisMonth, thisMonth.AddMonths(1), $"{ayAdlari[thisMonth.Month]} {thisMonth.Year}");
}

app.MapPost("/api/ai/toplam-avans", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var query = db.CalisanAvanslari
        .Where(x =>
            x.Tip == CalisanHareketTipi.Avans &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var tumAvanslar = await query
        .Select(x => new
        {
            x.Id,
            x.CalisanId,
            x.Tarih,
            x.Tutar,
            x.ArsivlendiMi
        })
        .ToListAsync();

    var arsivliCalisanIds = tumAvanslar
        .Where(x => x.ArsivlendiMi)
        .Select(x => x.CalisanId)
        .Distinct()
        .ToList();

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x => arsivliCalisanIds.Contains(x.CalisanId));

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var maasArsivleri = await arsivQuery
        .Select(x => new
        {
            x.CalisanId,
            x.OdemeTarihi
        })
        .ToListAsync();

    var liste = tumAvanslar
        .Where(x =>
        {
            if (!x.ArsivlendiMi)
                return true;

            var ayniAydaMaaslaKapanmisMi = maasArsivleri.Any(a =>
                a.CalisanId == x.CalisanId &&
                a.OdemeTarihi.Date >= x.Tarih.Date &&
                a.OdemeTarihi.Year == x.Tarih.Year &&
                a.OdemeTarihi.Month == x.Tarih.Month);

            return !ayniAydaMaaslaKapanmisMi;
        })
        .ToList();

    var toplam = liste.Sum(x => x.Tutar);

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = toplam > 0
            ? $"{ayAdi} ayında verilen toplam avans: {toplam:N2} TL\nDönem: {start:dd.MM.yyyy} - {end.AddDays(-1):dd.MM.yyyy}"
            : $"{ayAdi} ayında avans kaydı bulunamadı."
    });
});

app.MapPost("/api/ai/avans-dagilim", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var query = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.Tip == CalisanHareketTipi.Avans &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var tumAvanslar = await query
        .Select(x => new
        {
            x.Id,
            x.CalisanId,
            x.Tarih,
            Kisi = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            x.Tutar,
            x.ArsivlendiMi
        })
        .ToListAsync();

    var arsivliCalisanIds = tumAvanslar
        .Where(x => x.ArsivlendiMi)
        .Select(x => x.CalisanId)
        .Distinct()
        .ToList();

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x => arsivliCalisanIds.Contains(x.CalisanId));

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var maasArsivleri = await arsivQuery
        .Select(x => new
        {
            x.CalisanId,
            x.OdemeTarihi
        })
        .ToListAsync();

    var liste = tumAvanslar
        .Where(x =>
        {
            if (!x.ArsivlendiMi)
                return true;

            var ayniAydaMaaslaKapanmisMi = maasArsivleri.Any(a =>
                a.CalisanId == x.CalisanId &&
                a.OdemeTarihi.Date >= x.Tarih.Date &&
                a.OdemeTarihi.Year == x.Tarih.Year &&
                a.OdemeTarihi.Month == x.Tarih.Month);

            return !ayniAydaMaaslaKapanmisMi;
        })
        .OrderBy(x => x.Kisi)
        .ThenBy(x => x.Tarih)
        .ThenBy(x => x.Id)
        .ToList();

    if (!liste.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında avans kaydı bulunamadı."
        });
    }

    var toplam = liste.Sum(x => x.Tutar);
    var mesaj = $"{ayAdi} ayı avans detayları ({start:dd.MM.yyyy} - {end.AddDays(-1):dd.MM.yyyy}):\n\n";

    foreach (var grup in liste.GroupBy(x => x.Kisi))
    {
        mesaj += $"{grup.Key}:\n";

        foreach (var item in grup)
            mesaj += $"- {item.Tarih:dd.MM.yyyy}: {item.Tutar:N2} TL\n";

        mesaj += $"Toplam: {grup.Sum(x => x.Tutar):N2} TL\n\n";
    }

    mesaj += $"Genel toplam: {toplam:N2} TL";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = toplam,
        Message = mesaj
    });
});

app.MapPost("/api/ai/en-cok-avans-alan", async (AppDbContext db, CalisanAvansApiRequest req) =>
{
    int year = req.Year ?? DateTime.Now.Year;
    int month = req.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var query = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.Tip == CalisanHareketTipi.Avans &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var tumAvanslar = await query
        .Select(x => new
        {
            x.Id,
            x.CalisanId,
            x.Tarih,
            Kisi = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            x.Tutar,
            x.ArsivlendiMi
        })
        .ToListAsync();

    var arsivliCalisanIds = tumAvanslar
        .Where(x => x.ArsivlendiMi)
        .Select(x => x.CalisanId)
        .Distinct()
        .ToList();

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x => arsivliCalisanIds.Contains(x.CalisanId));

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var maasArsivleri = await arsivQuery
        .Select(x => new
        {
            x.CalisanId,
            x.OdemeTarihi
        })
        .ToListAsync();

    var liste = tumAvanslar
        .Where(x =>
        {
            if (!x.ArsivlendiMi)
                return true;

            var ayniAydaMaaslaKapanmisMi = maasArsivleri.Any(a =>
                a.CalisanId == x.CalisanId &&
                a.OdemeTarihi.Date >= x.Tarih.Date &&
                a.OdemeTarihi.Year == x.Tarih.Year &&
                a.OdemeTarihi.Month == x.Tarih.Month);

            return !ayniAydaMaaslaKapanmisMi;
        })
        .GroupBy(x => x.Kisi)
        .Select(g => new
        {
            Kisi = g.Key,
            Toplam = g.Sum(x => x.Tutar)
        })
        .Where(x => x.Toplam > 0)
        .OrderByDescending(x => x.Toplam)
        .ToList();

    var kisi = liste.FirstOrDefault();

    if (kisi == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında avans kaydı bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = kisi.Toplam,
        Message = $"{ayAdi} ayında en fazla avans alan çalışan: {kisi.Kisi} - {kisi.Toplam:N2} TL"
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

app.MapPost("/api/ai/personel-gideri", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var aktifMaasQuery = db.CalisanAvanslari
        .Include(x => x.Calisan)
        .Where(x =>
            x.Tip == CalisanHareketTipi.MaasOdeme &&
            !x.ArsivlendiMi &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        aktifMaasQuery = aktifMaasQuery.Where(x => x.FirmaId == firmaId);

    var aktifMaasListe = await aktifMaasQuery
        .Select(x => new
        {
            Calisan = x.Calisan != null ? x.Calisan.AdSoyad : x.Ad,
            x.Tutar
        })
        .ToListAsync();

    var aktifMaasToplam = aktifMaasListe
        .GroupBy(x => x.Calisan)
        .Select(g => g.Sum(x => x.Tutar))
        .Sum();

    var arsivMaasQuery = db.CalisanMaasArsivleri
        .Where(x =>
            x.OdemeTarihi >= start &&
            x.OdemeTarihi < end);

    if (firmaId != null)
        arsivMaasQuery = arsivMaasQuery.Where(x => x.FirmaId == firmaId);

    var arsivTumListe = await arsivMaasQuery
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                arsiv.Id,
                Calisan = calisan.AdSoyad,
                arsiv.OdemeTarihi,
                arsiv.ToplamMaas
            })
        .ToListAsync();

    var arsivMaasToplam = arsivTumListe
        .GroupBy(x => x.Calisan)
        .Select(g => g.OrderByDescending(x => x.OdemeTarihi).ThenByDescending(x => x.Id).First())
        .Sum(x => x.ToplamMaas);

    var avansQuery = db.CalisanAvanslari
        .Where(x =>
            x.Tip == CalisanHareketTipi.Avans &&
            x.Tarih >= start &&
            x.Tarih < end);

    if (firmaId != null)
        avansQuery = avansQuery.Where(x => x.FirmaId == firmaId);

    var avansToplam = await avansQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var maasToplam = aktifMaasToplam + arsivMaasToplam;
    var personelGideri = maasToplam;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = personelGideri,
        Message =
    $"{ayAdi} personel gideri:\n\n" +
    $"- Toplam maaş gideri: {maasToplam:N2} TL\n" +
    $"- Maaşlardan düşülen avans: {avansToplam:N2} TL\n" +
    $"- Net personel gideri: {personelGideri:N2} TL\n" +
    $"Not: Avans maaştan düşüldüğü için personel giderine ikinci kez eklenmedi."
    });
});

app.MapPost("/api/ai/ortalama-maas", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var query = db.CalisanMaasArsivleri
        .Where(x =>
            x.OdemeTarihi >= start &&
            x.OdemeTarihi < end);

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var tumListe = await query
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                arsiv.Id,
                Calisan = calisan.AdSoyad,
                arsiv.OdemeTarihi,
                arsiv.ToplamMaas
            })
        .ToListAsync();

    var liste = tumListe
        .GroupBy(x => x.Calisan)
        .Select(g => g.OrderByDescending(x => x.OdemeTarihi).ThenByDescending(x => x.Id).First())
        .Select(x => x.ToplamMaas)
        .ToList();

    if (!liste.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında maaş arşivi bulunamadı."
        });
    }

    var ortalama = liste.Average();

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = ortalama,
        Message = $"{ayAdi} ayı ortalama maaş: {ortalama:N2} TL\nHesaplanan çalışan sayısı: {liste.Count}\n"
    });
});

app.MapPost("/api/ai/son-maas-odemesi", async (AppDbContext db) =>
{
    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var query = db.CalisanMaasArsivleri.AsQueryable();

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var son = await query
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                Calisan = calisan.AdSoyad,
                arsiv.OdemeTarihi,
                arsiv.ToplamMaas,
                arsiv.ToplamAvans,
                arsiv.KalanMaas
            })
        .OrderByDescending(x => x.OdemeTarihi)
        .FirstOrDefaultAsync();

    if (son == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = "Henüz maaş ödeme arşivi bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = son.ToplamMaas,
        Message =
            $"Son maaş ödemesi:\n\n" +
            $"- Çalışan: {son.Calisan}\n" +
            $"- Ödeme tarihi: {son.OdemeTarihi:dd.MM.yyyy HH:mm}\n" +
            $"- Maaş: {son.ToplamMaas:N2} TL\n" +
            $"- Avans: {son.ToplamAvans:N2} TL\n" +
            $"- Kalan: {son.KalanMaas:N2} TL"
    });
});

app.MapPost("/api/ai/calisan-kalan-maas", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.CalisanAdi))
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = false,
            Total = 0,
            Message = "Çalışan adı gerekli."
        });
    }

    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var ad = request.CalisanAdi.ToLower();

    var calisanQuery = db.Calisanlar.AsQueryable();

    if (firmaId != null)
        calisanQuery = calisanQuery.Where(x => x.FirmaId == firmaId);

    var calisan = await calisanQuery
        .FirstOrDefaultAsync(x =>
            x.AdSoyad.ToLower().Contains(ad) ||
            x.Ad.ToLower().Contains(ad));

    if (calisan == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = false,
            Total = 0,
            Message = $"{request.CalisanAdi} isimli çalışan bulunamadı."
        });
    }

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x => x.CalisanId == calisan.Id &&
                    x.OdemeTarihi >= start &&
                    x.OdemeTarihi < end);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsiv = await arsivQuery
        .OrderByDescending(x => x.OdemeTarihi)
        .FirstOrDefaultAsync();

    if (arsiv == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{calisan.AdSoyad} için {ayAdi} ayında maaş arşivi bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = arsiv.KalanMaas,
        Message =
            $"{calisan.AdSoyad} için {ayAdi} maaş özeti:\n\n" +
            $"- Toplam maaş: {arsiv.ToplamMaas:N2} TL\n" +
            $"- Toplam avans: {arsiv.ToplamAvans:N2} TL\n" +
            $"- Kalan maaş: {arsiv.KalanMaas:N2} TL\n" +
            $"- Ödeme tarihi: {arsiv.OdemeTarihi:dd.MM.yyyy HH:mm}"
    });
});

app.MapPost("/api/ai/maas-avans-orani", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var query = db.CalisanMaasArsivleri
        .Where(x =>
            x.OdemeTarihi >= start &&
            x.OdemeTarihi < end);

    if (firmaId != null)
        query = query.Where(x => x.FirmaId == firmaId);

    var tumListe = await query
        .Join(
            db.Calisanlar,
            arsiv => arsiv.CalisanId,
            calisan => calisan.Id,
            (arsiv, calisan) => new
            {
                arsiv.Id,
                Calisan = calisan.AdSoyad,
                arsiv.OdemeTarihi,
                arsiv.ToplamMaas,
                arsiv.ToplamAvans
            })
        .ToListAsync();

    var liste = tumListe
        .GroupBy(x => x.Calisan)
        .Select(g => g.OrderByDescending(x => x.OdemeTarihi).ThenByDescending(x => x.Id).First())
        .ToList();

    var toplamMaas = liste.Sum(x => x.ToplamMaas);
    var toplamAvans = liste.Sum(x => x.ToplamAvans);

    if (toplamMaas <= 0)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında maaş arşivi bulunamadı."
        });
    }

    var oran = toplamAvans / toplamMaas * 100;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = oran,
        Message =
            $"{ayAdi} maaşa göre avans oranı: %{oran:N2}\n\n" +
            $"- Toplam maaş: {toplamMaas:N2} TL\n" +
            $"- Toplam avans: {toplamAvans:N2} TL\n" +
            $""
    });
});

app.MapPost("/api/ai/maasi-kapanmayan-calisanlar", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var calisanlarQuery = db.Calisanlar.AsQueryable();

    if (firmaId != null)
        calisanlarQuery = calisanlarQuery.Where(x => x.FirmaId == firmaId);

    var calisanlar = await calisanlarQuery
        .OrderBy(x => x.AdSoyad)
        .Select(x => new { x.Id, x.AdSoyad })
        .ToListAsync();

    var arsivlenenIdsQuery = db.CalisanMaasArsivleri
        .Where(x => x.OdemeTarihi >= start &&
                    x.OdemeTarihi < end);

    if (firmaId != null)
        arsivlenenIdsQuery = arsivlenenIdsQuery.Where(x => x.FirmaId == firmaId);

    var arsivlenenIds = await arsivlenenIdsQuery
        .Select(x => x.CalisanId)
        .Distinct()
        .ToListAsync();

    var kapanmayanlar = calisanlar
        .Where(x => !arsivlenenIds.Contains(x.Id))
        .ToList();

    if (!kapanmayanlar.Any())
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} ayında maaşı kapanmayan çalışan bulunmuyor."
        });
    }

    var mesaj = $"{ayAdi} ayında maaşı henüz kapanmayan çalışanlar:\n\n";

    foreach (var item in kapanmayanlar)
        mesaj += $"- {item.AdSoyad}\n";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = kapanmayanlar.Count,
        Message = mesaj
    });
});

app.MapPost("/api/ai/calisan-maas-ozet", async (CalisanAvansApiRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.CalisanAdi))
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = false,
            Total = 0,
            Message = "Çalışan adı gerekli."
        });
    }

    int year = request.Year ?? DateTime.Now.Year;
    int month = request.Month ?? DateTime.Now.Month;

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

    var firmaId = await db.Firmalar
        .Where(x => x.AktifMi)
        .OrderBy(x => x.Id)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync();

    var ayAdlari = new[] { "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };
    var ayAdi = ayAdlari[month];

    var ad = request.CalisanAdi.ToLower();

    var calisanQuery = db.Calisanlar.AsQueryable();

    if (firmaId != null)
        calisanQuery = calisanQuery.Where(x => x.FirmaId == firmaId);

    var calisan = await calisanQuery
        .FirstOrDefaultAsync(x =>
            x.AdSoyad.ToLower().Contains(ad) ||
            x.Ad.ToLower().Contains(ad));

    if (calisan == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = false,
            Total = 0,
            Message = $"{request.CalisanAdi} isimli çalışan bulunamadı."
        });
    }

    var arsivQuery = db.CalisanMaasArsivleri
        .Where(x => x.CalisanId == calisan.Id &&
                    x.OdemeTarihi >= start &&
                    x.OdemeTarihi < end);

    if (firmaId != null)
        arsivQuery = arsivQuery.Where(x => x.FirmaId == firmaId);

    var arsiv = await arsivQuery
        .OrderByDescending(x => x.OdemeTarihi)
        .FirstOrDefaultAsync();

    if (arsiv == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{calisan.AdSoyad} için {ayAdi} ayında maaş arşivi bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = arsiv.KalanMaas,
        Message =
            $"{calisan.AdSoyad} {ayAdi} maaş özeti:\n\n" +
            $"- Toplam maaş: {arsiv.ToplamMaas:N2} TL\n" +
            $"- Toplam avans: {arsiv.ToplamAvans:N2} TL\n" +
            $"- Kalan maaş: {arsiv.KalanMaas:N2} TL\n" +
            $"- Dönem: {arsiv.DonemBaslangic:dd.MM.yyyy} - {arsiv.DonemBitis:dd.MM.yyyy}\n" +
            $"- Ödeme tarihi: {arsiv.OdemeTarihi:dd.MM.yyyy HH:mm}"
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

    var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    var end = start.AddMonths(1);

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
        x.Tarih >= start &&
        x.Tarih < end);

    if (firmaId != null)
        aktifQuery = aktifQuery.Where(x => x.FirmaId == firmaId);

    var aktifToplam = await aktifQuery.SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var arsivQuery = db.CalisanMaasArsivleri.Where(x =>
        x.CalisanId == calisan.Id &&
        x.OdemeTarihi >= start &&
        x.OdemeTarihi < end);

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

// =========================
// EK AI ANALİZ ENDPOINTLERİ
// =========================

app.MapPost("/api/ai/kasa-artis-azalis", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var giris = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var cikis = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var sonuc = giris - cikis;
    var yorum = sonuc > 0 ? "Kasa bu dönemde artmış görünüyor." : sonuc < 0 ? "Kasa bu dönemde azalmış görünüyor." : "Bu dönemde kasa artışı veya azalışı görünmüyor.";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = sonuc,
        Message =
            $"{ayAdi} kasa artış/azalış analizi:\n\n" +
            $"- Toplam giriş: {giris:N2} TL\n" +
            $"- Toplam çıkış: {cikis:N2} TL\n" +
            $"- Net sonuç: {sonuc:N2} TL\n" +
            $"- Yorum: {yorum}"
    });
});

app.MapPost("/api/ai/son-7-gun-kasa-ozeti", async (AppDbContext db) =>
{
    var bitis = DateTime.UtcNow.Date.AddDays(1);
    var baslangic = bitis.AddDays(-7);

    var giris = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var cikis = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var islemSayisi = await db.KasaHareketleri.CountAsync(x => x.Tarih >= baslangic && x.Tarih < bitis);
    var sonuc = giris - cikis;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = sonuc,
        Message =
            $"Son 7 gün kasa özeti:\n\n" +
            $"- Toplam giriş: {giris:N2} TL\n" +
            $"- Toplam çıkış: {cikis:N2} TL\n" +
            $"- Net sonuç: {sonuc:N2} TL\n" +
            $"- İşlem sayısı: {islemSayisi}"
    });
});

app.MapPost("/api/ai/gunluk-ortalama-gider", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var toplamGider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var gunSayisi = Math.Max(1, (bitis.Date - baslangic.Date).Days);
    var ortalama = toplamGider / gunSayisi;

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = ortalama,
        Message =
            $"{ayAdi} günlük ortalama gider:\n\n" +
            $"- Toplam gider: {toplamGider:N2} TL\n" +
            $"- Gün sayısı: {gunSayisi}\n" +
            $"- Günlük ortalama gider: {ortalama:N2} TL"
    });
});

app.MapPost("/api/ai/en-cok-devamsizlik-yapan", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var sonuc = await db.Set<CalisanPuantaj>()
        .Include(x => x.Calisan)
        .Where(x => x.Tarih >= baslangic && x.Tarih < bitis && x.Durum == PuantajDurum.Gelmedi)
        .GroupBy(x => x.Calisan != null ? x.Calisan.AdSoyad : "Bilinmeyen çalışan")
        .Select(g => new { Calisan = g.Key, Gun = g.Count() })
        .OrderByDescending(x => x.Gun)
        .FirstOrDefaultAsync();

    if (sonuc == null)
    {
        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = 0,
            Message = $"{ayAdi} devamsızlık kaydı bulunamadı."
        });
    }

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = sonuc.Gun,
        Message = $"{ayAdi} en fazla devamsızlık yapan çalışan: {sonuc.Calisan} - {sonuc.Gun} gün"
    });
});

app.MapPost("/api/ai/akilli-isletme-yorumu", async (AppDbContext db, CalisanAvansApiRequest request) =>
{
    var (baslangic, bitis, ayAdi) = GetDateRange(request);

    var gelir = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var gider = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis && x.Tarih >= baslangic && x.Tarih < bitis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var sonuc = gelir - gider;

    var personelGideri = await db.CalisanMaasArsivleri
        .Where(x => x.OdemeTarihi >= baslangic && x.OdemeTarihi < bitis)
        .SumAsync(x => (decimal?)x.ToplamMaas) ?? 0;

    var yorum = "";

    if (sonuc > 0)
        yorum += "Bu dönemde kasa sonucu pozitif görünüyor.\n";
    else if (sonuc < 0)
        yorum += "Bu dönemde kasa sonucu negatif görünüyor.\n";
    else
        yorum += "Bu dönemde kasa sonucu dengede görünüyor.\n";

    if (personelGideri > 0 && gider > 0)
    {
        var oran = personelGideri / gider * 100;
        yorum += $"Personel gideri, toplam giderlerin yaklaşık %{oran:N2} seviyesinde.\n";
    }

    if (gider > gelir && gelir > 0)
        yorum += "Giderler gelirlerden yüksek olduğu için harcama kalemleri ayrıca incelenmeli.\n";
    else if (gelir > gider)
        yorum += "Gelirler giderlerden yüksek olduğu için dönem olumlu görünüyor.\n";

    return Results.Json(new CalisanAvansToplamResponse
    {
        Success = true,
        Total = sonuc,
        Message =
            $"{ayAdi} akıllı işletme yorumu:\n\n" +
            $"- Gelir: {gelir:N2} TL\n" +
            $"- Gider: {gider:N2} TL\n" +
            $"- Net sonuç: {sonuc:N2} TL\n" +
            $"- Personel gideri: {personelGideri:N2} TL\n\n" +
            yorum
    });
});

app.MapRazorPages();

app.Run();

static async Task<IResult> GetCalisanPuantajOzetiAsync(AppDbContext db, CalisanAvansApiRequest req)
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.CalisanAdi))
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = false,
                Total = 0,
                Message = "Çalışan adı gerekli."
            });
        }

        int year = req.Year ?? DateTime.UtcNow.Year;
        int month = req.Month ?? DateTime.UtcNow.Month;

        var ayBaslangic = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sonrakiAy = ayBaslangic.AddMonths(1);
        var ayBitis = sonrakiAy.AddDays(-1);

        var firmaId = await db.Firmalar
            .Where(x => x.AktifMi)
            .OrderBy(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        var ad = (req.CalisanAdi ?? "").Trim().ToLower();

        var calisanlarQuery = db.Calisanlar.AsQueryable();

        if (firmaId != null)
            calisanlarQuery = calisanlarQuery.Where(x => x.FirmaId == firmaId);

        var calisanlar = await calisanlarQuery.ToListAsync();

        var calisan = calisanlar.FirstOrDefault(x =>
        {
            var tamAd = (x.AdSoyad ?? "").Trim().ToLower();
            var kisaAd = (x.Ad ?? "").Trim().ToLower();

            return tamAd == ad
                || tamAd.StartsWith(ad + " ")
                || kisaAd == ad;
        });

        if (calisan == null)
        {
            return Results.Json(new CalisanAvansToplamResponse
            {
                Success = false,
                Total = 0,
                Message = $"{req.CalisanAdi} çalışanı bulunamadı."
            });
        }

        var kayitlarQuery = db.CalisanPuantajlari
            .Where(x =>
                x.CalisanId == calisan.Id &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < sonrakiAy);

        if (firmaId != null)
            kayitlarQuery = kayitlarQuery.Where(x => x.FirmaId == firmaId);

        var kayitlar = await kayitlarQuery.ToListAsync();

        int geldi = 0;
        int gelmedi = 0;
        int izinli = 0;
        int yarimGun = 0;

        var bugun = DateTime.UtcNow.Date;

        for (var gun = ayBaslangic; gun <= ayBitis; gun = gun.AddDays(1))
        {
            if (gun.DayOfWeek == DayOfWeek.Sunday)
                continue;

            if (gun.Date > bugun)
                continue;

            var kayit = kayitlar.FirstOrDefault(x =>
                x.Tarih >= gun &&
                x.Tarih < gun.AddDays(1));

            var durum = kayit != null
                ? kayit.Durum
                : PuantajDurum.Gelmedi;

            switch (durum)
            {
                case PuantajDurum.Geldi:
                    geldi++;
                    break;
                case PuantajDurum.Gelmedi:
                    gelmedi++;
                    break;
                case PuantajDurum.Izinli:
                    izinli++;
                    break;
                case PuantajDurum.YarimGun:
                    yarimGun++;
                    break;
            }
        }

        var ayAdlari = new[]
        {
            "", "Ocak", "Şubat", "Mart", "Nisan",
            "Mayıs", "Haziran", "Temmuz", "Ağustos",
            "Eylül", "Ekim", "Kasım", "Aralık"
        };

        var ayAdi = ayAdlari[month];

        return Results.Json(new CalisanAvansToplamResponse
        {
            Success = true,
            Total = gelmedi,
            Message =
                $"{calisan.AdSoyad} {ayAdi} {year} puantaj/devamsızlık özeti:\n" +
                $"- Geldi: {geldi} gün\n" +
                $"- Gelmedi: {gelmedi} gün\n" +
                $"- İzinli: {izinli} gün\n" +
                $"- Yarım gün: {yarimGun} gün"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            message = "Puantaj alınırken hata oluştu.",
            error = ex.Message,
            detail = ex.InnerException?.Message
        }, statusCode: 500);
    }
}

public class CalisanAvansApiRequest
{
    public string CalisanAdi { get; set; } = "";
    public string DateRange { get; set; } = "ThisMonth";
    public int? Year { get; set; }
    public int? Month { get; set; }
}