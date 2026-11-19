using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Data;

internal static class CompanyFileModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<CompanyStoredFile>(entity =>
        {
            entity.ToTable("CompanyStoredFiles", table =>
                table.HasCheckConstraint("CK_CompanyStoredFiles_Length", "\"Length\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.Id, x.OwnerCompanyId });
            entity.Property(x => x.Sha256).HasMaxLength(64);
        });

        model.Entity<EkDosya>().HasAlternateKey(x => new { x.Id, x.FirmaId });
        model.Entity<Cek>().HasAlternateKey(x => new { x.Id, x.FirmaId });
        model.Entity<CompanyStoredFileReference>(entity =>
        {
            entity.ToTable("CompanyStoredFileReferences", table =>
                table.HasCheckConstraint("CK_CompanyStoredFileReferences_Source",
                    """("AttachmentId" IS NULL) <> ("CheckId" IS NULL)"""));
            entity.Property<long>("Id").ValueGeneratedOnAdd();
            entity.HasKey("Id");
            entity.HasIndex(x => x.AttachmentId).IsUnique();
            entity.HasIndex(x => x.CheckId).IsUnique();
            entity.HasOne<CompanyStoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EkDosya>().WithMany().HasForeignKey(x => new { x.AttachmentId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.FirmaId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Cek>().WithMany().HasForeignKey(x => new { x.CheckId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.FirmaId }).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<CompanyFileCleanupJob>(entity =>
        {
            entity.ToTable("CompanyFileCleanupJobs", table =>
                table.HasCheckConstraint("CK_CompanyFileCleanupJobs_Company", "\"CompanyId\" > 1"));
            entity.HasKey(x => x.FileId);
            entity.HasIndex(x => new { x.CompletedAtUtc, x.EnqueuedAtUtc });
            entity.HasOne<CompanyStoredFile>().WithMany().HasForeignKey(x => new { x.FileId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerCompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
