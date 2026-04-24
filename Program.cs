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

    // API isteklerini login kontrolünden çıkar
    if (path.StartsWith("/api"))
    {
        await next();
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

app.MapPost("/api/ai/calisan-avans-toplam", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetCalisanAvansToplamAsync(db, request.CalisanAdi, request.DateRange);
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

app.MapPost("/api/ai/toplam-avans", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        var result = await AiApiHelpers.GetToplamAvansAsync(db, request.DateRange);
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

// =========================
// YENİ: TOPLAM GELİR
// =========================
app.MapPost("/api/ai/toplam-gelir", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        DateTime baslangic;
        DateTime bitis;
        var now = DateTime.UtcNow;

        if (request.DateRange == "Today")
        {
            baslangic = now.Date;
            bitis = baslangic.AddDays(1);
        }
        else if (request.DateRange == "LastMonth")
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
            bitis = baslangic.AddMonths(1);
        }
        else
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            bitis = baslangic.AddMonths(1);
        }

        var toplam = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Giris &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return Results.Json(new
        {
            success = true,
            total = toplam,
            message = $"Toplam gelir: {toplam:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message
        }, statusCode: 500);
    }
});

// =========================
// YENİ: TOPLAM GİDER
// =========================
app.MapPost("/api/ai/toplam-gider", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        DateTime baslangic;
        DateTime bitis;
        var now = DateTime.UtcNow;

        if (request.DateRange == "Today")
        {
            baslangic = now.Date;
            bitis = baslangic.AddDays(1);
        }
        else if (request.DateRange == "LastMonth")
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
            bitis = baslangic.AddMonths(1);
        }
        else
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            bitis = baslangic.AddMonths(1);
        }

        var toplam = await db.KasaHareketleri
            .Where(x => x.Tip == HareketTipi.Cikis &&
                        x.Tarih >= baslangic &&
                        x.Tarih < bitis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        return Results.Json(new
        {
            success = true,
            total = toplam,
            message = $"Toplam gider: {toplam:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message
        }, statusCode: 500);
    }
});

// =========================
// YENİ: KASA BAKİYE
// =========================
app.MapPost("/api/ai/kasa-bakiye", async (CalisanAvansToplamRequest request, AppDbContext db) =>
{
    try
    {
        DateTime baslangic;
        DateTime bitis;
        var now = DateTime.UtcNow;

        if (request.DateRange == "Today")
        {
            baslangic = now.Date;
            bitis = baslangic.AddDays(1);
        }
        else if (request.DateRange == "LastMonth")
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
            bitis = baslangic.AddMonths(1);
        }
        else
        {
            baslangic = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            bitis = baslangic.AddMonths(1);
        }

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

        return Results.Json(new
        {
            success = true,
            total = bakiye,
            message = $"Kasa bakiyesi: {bakiye:N2} TL"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message
        }, statusCode: 500);
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

app.MapRazorPages();

app.Run();