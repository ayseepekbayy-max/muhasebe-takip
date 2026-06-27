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
    public DbSet<Fatura> Faturalar { get; set; } = default!;
    public DbSet<FaturaKalem> FaturaKalemleri { get; set; } = default!;
    public DbSet<FaturaNumaraAyari> FaturaNumaraAyarlari { get; set; } = default!;
    public DbSet<Calisan> Calisanlar { get; set; } = default!;
    public DbSet<CalisanAvans> CalisanAvanslari { get; set; } = default!;
    public DbSet<Musteri> Musteriler { get; set; } = default!;
    public DbSet<MusteriIs> MusteriIsler { get; set; } = default!;
    public DbSet<MusteriMasraf> MusteriMasraflar { get; set; } = default!;
    public DbSet<StokUrun> StokUrunler { get; set; } = default!;
    public DbSet<StokHareket> StokHareketleri { get; set; } = default!;
    public DbSet<Cek> Cekler { get; set; } = default!;
    public DbSet<CalisanPuantaj> CalisanPuantajlari { get; set; } = default!;
    public DbSet<CalisanMaasArsiv> CalisanMaasArsivleri { get; set; } = default!;
    public DbSet<MaliyetKaydi> MaliyetKayitlari { get; set; } = default!;
    public DbSet<EkDosya> EkDosyalar { get; set; } = default!;
    public DbSet<IslemGecmisi> IslemGecmisleri { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IslemGecmisi>(entity =>
        {
            entity.HasIndex(x => new { x.FirmaId, x.Tarih });
            entity.HasIndex(x => new { x.FirmaId, x.Modul });

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Fatura>(entity =>
        {
            entity.HasIndex(x => new { x.FirmaId, x.FaturaNo })
                .IsUnique();
        });
    }
}
