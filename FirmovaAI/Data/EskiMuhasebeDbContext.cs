using Microsoft.EntityFrameworkCore;
using FirmovaAI.Models;

namespace FirmovaAI.Data
{
    public class EskiMuhasebeDbContext : DbContext
    {
        public EskiMuhasebeDbContext(DbContextOptions<EskiMuhasebeDbContext> options)
            : base(options)
        {
        }

        public DbSet<CariKart> CariKartlar => Set<CariKart>();
        public DbSet<Calisan> Calisanlar => Set<Calisan>();
        public DbSet<StokUrun> StokUrunler => Set<StokUrun>();
        public DbSet<KasaHareket> KasaHareketler => Set<KasaHareket>();
    }
}