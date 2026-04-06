using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".MuhasebeTakip2.Session.v2";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(cs);
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

// Eski verileri mevcut firmaya bağla
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var firma = db.Firmalar.FirstOrDefault();

    if (firma != null)
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

app.UseAuthorization();

app.MapRazorPages();

app.Run();