using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

// Only the missing combination: unchanged recovery + final file gates/model + real runtime ACLs.
internal static class PackageRecoveryCompatibility
{
    internal static async Task RunAsync()
    {
        const string connection="Host=/work;Database=firmova_stage10_synthetic;Username=firmova_app_runtime;Pooling=false";
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        db.Add(new Firma{Id=75,FirmaAdi="Synthetic recovery compatibility"});
        db.Add(new Kullanici{Id=104,FirmaId=75,KullaniciAdi="synthetic-recovery"});
        db.Add(new Fatura{FirmaId=75,FaturaNo="synthetic-recovery-invoice"});
        if(!await db.Faturalar.AnyAsync(x=>x.FirmaId==74)) db.Add(new Fatura{FirmaId=74,FaturaNo="preserve-fixture"});
        await db.SaveChangesAsync();
        var invoice=await db.Faturalar.SingleAsync(x=>x.FirmaId==75);
        var env=new FixtureEnvironment();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["CompanyDocuments:WritePrivate"]="true"}).Build();
        var checkUpload=AccountingDocuments.PrepareUpload(env,config,"cekler",$"{Guid.NewGuid():D}.png");
        var attachmentUpload=AccountingDocuments.PrepareUpload(env,config,"faturalar",$"fatura-{invoice.Id}-{Guid.NewGuid():N}.pdf");
        var image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aQ34AAAAASUVORK5CYII=");
        var pdf="%PDF-1.4\n% synthetic package recovery\n%%EOF\n"u8.ToArray();
        await File.WriteAllBytesAsync(checkUpload.PhysicalPath,image);
        await File.WriteAllBytesAsync(attachmentUpload.PhysicalPath,pdf);
        var check=new Cek{FirmaId=75,No="synthetic-recovery-check",Tarih=DateTime.UtcNow,ResimYolu=checkUpload.StoredPath};
        var attachment=new EkDosya{FirmaId=75,FaturaId=invoice.Id,DosyaYolu=attachmentUpload.StoredPath,DosyaAdi="synthetic.pdf",Boyut=pdf.Length};
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            db.AddRange(check,attachment);await db.SaveChangesAsync();
            var store=new CompanyDocumentStore(db,env);
            await store.RegisterPublishedAsync(checkUpload.StoredPath,null,check,default);
            await store.RegisterPublishedAsync(attachmentUpload.StoredPath,attachment,null,default);
            await tx.CommitAsync();
        }
        var key=RandomNumberGenerator.GetBytes(32); // Synthetic, memory only; no production key source.
        try
        {
            using var archive=CompanyRecoveryArchive.OnCompanyDisk(env,key,"synthetic-package");
            var backup=new AccountingBackupService(db,env,writePrivate:true);
            var recovery=new CompanyAccountingRecovery(backup,archive);
            var operation=await recovery.CreateAsync(75,default);
            var envelope=archive.ReadVerifiedAccountingEnvelope(75,operation);
            // Only synthetic accounting rows are cleared; company/users and original files remain.
            await using(var tx=await db.Database.BeginTransactionAsync())
            {
                await db.EkDosyalar.Where(x=>x.FirmaId==75).ExecuteDeleteAsync();
                await db.Cekler.Where(x=>x.FirmaId==75).ExecuteDeleteAsync();
                await db.Faturalar.Where(x=>x.FirmaId==75).ExecuteDeleteAsync();
                await tx.CommitAsync();
            }
            db.ChangeTracker.Clear();
            using var uploaded=new MemoryStream(envelope,writable:false);
            await recovery.RestoreUploadedAsync(75,operation,uploaded,envelope.Length,default);
            var restoredCheck=await db.Cekler.AsNoTracking().SingleAsync(x=>x.FirmaId==75);
            var restoredAttachment=await db.EkDosyalar.AsNoTracking().SingleAsync(x=>x.FirmaId==75);
            var imagePath=AccountingDocuments.ResolveDocument(env.WebRootPath,restoredCheck.ResimYolu!,"cekler","")
                ?? throw new InvalidDataException("Restored cheque path was rejected.");
            var pdfPath=AccountingDocuments.ResolveDocument(env.WebRootPath,restoredAttachment.DosyaYolu,"faturalar","")
                ?? throw new InvalidDataException("Restored attachment path was rejected.");
            var restoredImage=await File.ReadAllBytesAsync(imagePath);
            var restoredPdf=await File.ReadAllBytesAsync(pdfPath);
            if(!image.SequenceEqual(restoredImage) || !pdf.SequenceEqual(restoredPdf) ||
               !await db.Faturalar.AnyAsync(x=>x.FirmaId==74 && x.FaturaNo=="preserve-fixture"))
                throw new InvalidDataException("Recovery compatibility changed bytes or another company.");
            // Restored legacy/recovery links must not be guessed into trusted deletion ownership.
            try
            {
                await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(75,72,101,Guid.NewGuid(),new CompanyDocumentStore(db,env),default);
                throw new InvalidOperationException("Unowned restored documents became deletable.");
            }
            catch(InvalidDataException) { }
            if(!await db.Firmalar.AnyAsync(x=>x.Id==75) || !File.Exists(imagePath) || !File.Exists(pdfPath))
                throw new InvalidDataException("Restored ownership gate changed records/files.");
            CryptographicOperations.ZeroMemory(envelope);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        Console.WriteLine("PASS package recovery compatibility: unchanged encrypted upload/restore, final file gates and least-privilege runtime; restored image/PDF exact; unowned restore blocks deletion");
    }

    private sealed class FixtureEnvironment:IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="Synthetic";
        public string EnvironmentName{get;set;}="Testing";
        public string ContentRootPath{get;set;}="/recovery";
        public string WebRootPath{get;set;}="/recovery/wwwroot";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
