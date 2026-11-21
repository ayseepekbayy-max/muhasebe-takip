using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Data;

internal static class EmployeeIdentityModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<Calisan>().Property(x => x.FirmaId).IsRequired();
        model.Entity<Calisan>().HasAlternateKey(x => new { x.Id, x.FirmaId });
        // Preserve the existing company FK's NO ACTION behavior; requiredness must not add cascading deletion.
        model.Entity<Calisan>().HasOne(x => x.Firma).WithMany().HasForeignKey(x => x.FirmaId)
            .OnDelete(DeleteBehavior.ClientSetNull);
        model.Entity<CalisanAvans>().Property(x => x.FirmaId).IsRequired();
        model.Entity<CalisanAvans>().HasOne(x => x.Firma).WithMany().HasForeignKey(x => x.FirmaId)
            .OnDelete(DeleteBehavior.ClientSetNull);
        model.Entity<CalisanAvans>().HasIndex(x => x.CalisanId);
        model.Entity<CalisanPuantaj>().HasIndex(x => x.CalisanId);
        model.Entity<CalisanAvans>().HasOne(x => x.Calisan).WithMany(x => x.Avanslar)
            .HasForeignKey(x => new { x.CalisanId, x.FirmaId })
            .HasPrincipalKey(x => new { x.Id, x.FirmaId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CalisanPuantaj>().HasOne(x => x.Calisan).WithMany()
            .HasForeignKey(x => new { x.CalisanId, x.FirmaId })
            .HasPrincipalKey(x => new { x.Id, x.FirmaId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CalisanMaasArsiv>().HasOne<Calisan>().WithMany()
            .HasForeignKey(x => new { x.DogrulanmisCalisanId, x.FirmaId })
            .HasPrincipalKey(x => new { x.Id, x.FirmaId }).OnDelete(DeleteBehavior.Restrict);
    }
}
