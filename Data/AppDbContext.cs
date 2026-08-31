using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options) : base(options)
    {
    }

    // Request-scoped, server-side tenant established by the /api/ai middleware.
    // This is runtime context only; it is not part of the EF model.
    public int? AuthenticatedAiFirmaId { get; set; }

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
    public DbSet<OdemePlani> OdemePlanlari { get; set; } = default!;
    public DbSet<OdemeHareketi> OdemeHareketleri { get; set; } = default!;
    public DbSet<OdemeBildirimGecmisi> OdemeBildirimGecmisleri { get; set; } = default!;
    public DbSet<OdemeBildirimGizleme> OdemeBildirimGizlemeleri { get; set; } = default!;
    public DbSet<YoneticiNotu> YoneticiNotlari { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Kullanici>(entity =>
        {
            entity.Property(x => x.OdemeEmailBildirimiAktifMi).HasDefaultValue(true);
            entity.Property(x => x.EmailDogrulandiMi).HasDefaultValue(false);
        });

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

        modelBuilder.Entity<OdemePlani>(entity =>
        {
            entity.HasIndex(x => x.FirmaId);
            entity.HasIndex(x => new { x.FirmaId, x.AktifMi, x.SonrakiOdemeTarihi });
            entity.Property(x => x.AylikOdemeTutari).HasPrecision(18, 2);
            entity.Property(x => x.TamamlandiMi).HasDefaultValue(false);

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OdemeHareketi>(entity =>
        {
            entity.HasIndex(x => x.FirmaId);
            entity.HasIndex(x => new { x.FirmaId, x.OdemePlaniId, x.OdemeTarihi });
            entity.Property(x => x.Tutar).HasPrecision(18, 2);

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.OdemePlani)
                .WithMany(x => x.Hareketler)
                .HasForeignKey(x => x.OdemePlaniId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OdemeBildirimGecmisi>(entity =>
        {
            entity.HasIndex(x => x.FirmaId);
            entity.HasIndex(x => new { x.FirmaId, x.KullaniciId, x.OdemePlaniId, x.BildirimTuru, x.OdemeDonemi });
            entity.HasIndex(x => new { x.FirmaId, x.OdemePlaniId, x.BildirimTarihi });

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Kullanici)
                .WithMany()
                .HasForeignKey(x => x.KullaniciId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OdemePlani)
                .WithMany()
                .HasForeignKey(x => x.OdemePlaniId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        modelBuilder.Entity<OdemeBildirimGizleme>(entity =>
        {
            entity.HasIndex(x => x.FirmaId);
            entity.HasIndex(x => new { x.FirmaId, x.KullaniciId, x.OdemePlaniId, x.GizlemeTarihi })
                .IsUnique();

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Kullanici)
                .WithMany()
                .HasForeignKey(x => x.KullaniciId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OdemePlani)
                .WithMany()
                .HasForeignKey(x => x.OdemePlaniId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<YoneticiNotu>(entity =>
        {
            entity.HasIndex(x => new { x.FirmaId, x.SonKullanmaTarihi });
            entity.Property(x => x.NotMetni).HasMaxLength(500).IsRequired();

            entity.HasOne(x => x.Firma)
                .WithMany()
                .HasForeignKey(x => x.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Kullanici)
                .WithMany()
                .HasForeignKey(x => x.KullaniciId)
                .OnDelete(DeleteBehavior.Restrict);
        });

    }
}
