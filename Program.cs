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
app.MapPost("/api/ai/musteri-borc", async (AppDbContext db, CalisanAvansToplamRequest req) =>
{
    var result = await AiApiHelpers.GetMusteriBorcAsync(db, req.CalisanAdi);
    return Results.Json(result);
});
app.MapPost("/api/ai/musteri-sayisi", async (AppDbContext db) =>
{
    var count = await db.Musteriler.CountAsync();
    return Results.Json(new { success = true, message = $"Toplam müşteri sayısı: {count}" });
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
    return Results.Json(new { success = true, message = $"Toplam ürün sayısı: {count}" });
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
});

app.MapPost("/api/ai/en-cok-stokta-olan-urun", async (AppDbContext db) =>
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

app.MapPost("/api/ai/en-cok-stokta-olan-urun", async (AppDbContext db) =>
{
    var urun = await db.StokUrunler
        .Select(u => new
        {
            u.Ad,
            Miktar = db.StokHareketleri
                .Where(h => h.StokUrunId == u.Id)
                .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
        })
        .OrderByDescending(x => x.Miktar)
        .FirstOrDefaultAsync();

    if (urun == null)
        return Results.Json(new { message = "Ürün bulunamadı." });

    return Results.Json(new
    {
        message = $"En çok stokta olan ürün: {urun.Ad} ({urun.Miktar})"
    });
});

app.MapPost("/api/ai/biten-stoklar", async (AppDbContext db) =>
{
    var liste = await db.StokUrunler
        .Select(u => new
        {
            u.Ad,
            Miktar = db.StokHareketleri
                .Where(h => h.StokUrunId == u.Id)
                .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
        })
        .Where(x => x.Miktar <= 0)
        .ToListAsync();

    if (!liste.Any())
        return Results.Json(new { message = "Biten stok yok" });

    var metin = string.Join(", ", liste.Select(x => x.Ad));

    return Results.Json(new
    {
        message = $"Biten ürünler: {metin}"
    });
});

app.MapPost("/api/ai/stok-sayisi", async (AppDbContext db) =>
{
    var count = await db.StokUrunler.CountAsync();

    return Results.Json(new
    {
        message = $"Toplam stok ürün sayısı: {count}"
    });
});

// ==========================
// CORE AI API'LER
// ==========================

// GENEL ÖZET
app.MapPost("/api/ai/genel-ozet", async (AppDbContext db) =>
{
    var musteri = await db.Musteriler.CountAsync();
    var calisan = await db.Calisanlar.CountAsync();
    var cari = await db.CariKartlar.CountAsync();
    var stok = await db.StokUrunler.CountAsync();

    var giris = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var cikis = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var bakiye = giris - cikis;

    return Results.Json(new
    {
        message =
            $"Genel durum:\n" +
            $"- Kasa: {bakiye:N2} TL\n" +
            $"- Müşteri: {musteri}\n" +
            $"- Çalışan: {calisan}\n" +
            $"- Cari: {cari}\n" +
            $"- Stok ürün: {stok}"
    });
});


// KASA BAKİYE
app.MapPost("/api/ai/kasa-bakiye", async (AppDbContext db) =>
{
    var giris = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Giris)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    var cikis = await db.KasaHareketleri
        .Where(x => x.Tip == HareketTipi.Cikis)
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;

    return Results.Json(new
    {
        message = $"Kasa bakiyesi: {(giris - cikis):N2} TL"
    });
});


// BUGÜN KASA İŞLEM SAYISI
app.MapPost("/api/ai/bugun-kasa-islem-sayisi", async (AppDbContext db) =>
{
    var bugun = DateTime.Today;
    var yarin = bugun.AddDays(1);

    var count = await db.KasaHareketleri
        .CountAsync(x => x.Tarih >= bugun && x.Tarih < yarin);

    return Results.Json(new
    {
        message = $"Bugün {count} işlem yapıldı"
    });
});


// MÜŞTERİ SAYISI
app.MapPost("/api/ai/musteri-sayisi", async (AppDbContext db) =>
{
    return Results.Json(new
    {
        message = $"Toplam müşteri: {await db.Musteriler.CountAsync()}"
    });
});


// ÇALIŞAN SAYISI
app.MapPost("/api/ai/calisan-sayisi", async (AppDbContext db) =>
{
    return Results.Json(new
    {
        message = $"Toplam çalışan: {await db.Calisanlar.CountAsync()}"
    });
});


// CARİ SAYISI
app.MapPost("/api/ai/cari-sayisi", async (AppDbContext db) =>
{
    return Results.Json(new
    {
        message = $"Toplam cari: {await db.CariKartlar.CountAsync()}"
    });
});


// STOK SAYISI
app.MapPost("/api/ai/stok-sayisi", async (AppDbContext db) =>
{
    return Results.Json(new
    {
        message = $"Toplam ürün: {await db.StokUrunler.CountAsync()}"
    });
});


// BİTEN STOK
app.MapPost("/api/ai/biten-stoklar", async (AppDbContext db) =>
{
    var liste = await db.StokUrunler
        .Select(u => new
        {
            u.Ad,
            Miktar = db.StokHareketleri
                .Where(h => h.StokUrunId == u.Id)
                .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
        })
        .Where(x => x.Miktar <= 0)
        .ToListAsync();

    if (!liste.Any())
        return Results.Json(new { message = "Biten stok yok" });

    return Results.Json(new
    {
        message = "Biten ürünler: " + string.Join(", ", liste.Select(x => x.Ad))
    });
});


// EN ÇOK STOK
app.MapPost("/api/ai/en-cok-stokta-olan-urun", async (AppDbContext db) =>
{
    var urun = await db.StokUrunler
        .Select(u => new
        {
            u.Ad,
            Miktar = db.StokHareketleri
                .Where(h => h.StokUrunId == u.Id)
                .Sum(h => h.Tip == StokHareketTipi.Giris ? h.Miktar : -h.Miktar)
        })
        .OrderByDescending(x => x.Miktar)
        .FirstOrDefaultAsync();

    if (urun == null)
        return Results.Json(new { message = "Ürün yok" });

    return Results.Json(new
    {
        message = $"En çok stokta: {urun.Ad} ({urun.Miktar})"
    });
});

app.MapRazorPages();

app.Run();