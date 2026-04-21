using FirmovaAI.Models.Muhasebe;
using Microsoft.EntityFrameworkCore;

namespace FirmovaAI.Data;

public class MuhasebeDbContext : DbContext
{
    public MuhasebeDbContext(DbContextOptions<MuhasebeDbContext> options)
        : base(options)
    {
    }

    public DbSet<Calisan> Calisanlar => Set<Calisan>();
    public DbSet<CalisanAvans> CalisanAvanslari => Set<CalisanAvans>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Calisan>().ToTable("Calisanlar");
        modelBuilder.Entity<CalisanAvans>().ToTable("CalisanAvanslari");

        modelBuilder.Entity<CalisanAvans>()
            .HasOne(x => x.Calisan)
            .WithMany()
            .HasForeignKey(x => x.CalisanId);
    }
}