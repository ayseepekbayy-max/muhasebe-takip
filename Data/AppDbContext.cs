using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Firma> Firmalar { get; set; } = default!;
    public DbSet<Kullanici> Kullanicilar { get; set; } = default!;
    public DbSet<CariKart> CariKartlar { get; set; } = default!;
    public DbSet<KasaHareket> KasaHareketleri { get; set; } = default!;
    public DbSet<Calisan> Calisanlar { get; set; } = default!;
    public DbSet<CalisanAvans> CalisanAvanslari { get; set; } = default!;
    public DbSet<Musteri> Musteriler { get; set; } = default!;
    public DbSet<MusteriIs> MusteriIsler { get; set; } = default!;
    public DbSet<MusteriMasraf> MusteriMasraflar { get; set; } = default!;
    public DbSet<CalisanPuantaj> CalisanPuantajlari { get; set; } = default!;

    public DbSet<StokUrun> StokUrunler { get; set; } = default!;
    public DbSet<StokHareket> StokHareketleri { get; set; } = default!;

    public DbSet<Cek> Cekler { get; set; } = default!;
}