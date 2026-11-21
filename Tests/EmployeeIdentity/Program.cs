using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using MuhasebeTakip2.App.Migrations;
using EmployeePage=MuhasebeTakip2.App.Pages.Calisanlar.IndexModel;
using ArchivePage=MuhasebeTakip2.App.Pages.Calisanlar.ArsivModel;
using SalaryPage=MuhasebeTakip2.App.Pages.Calisanlar.Detay.IndexModel;
using AttendancePage=MuhasebeTakip2.App.Pages.Calisanlar.PuantajModel;
if(!OperatingSystem.IsLinux() || args is not ["test" or "metadata"])throw new InvalidOperationException("Synthetic Linux fixture only.");
const string connection="Host=/work;Database=firmova_employee_identity_synthetic;Username=postgres;Pooling=false";
var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
AppDbContext Db()=>new(options);
if(args[0]=="metadata")
{
 using var db=Db();
 var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();
 Microsoft.EntityFrameworkCore.Design.DesignTimeServiceCollectionExtensions.AddDbContextDesignTimeServices(services,db);
 var assembly=System.Reflection.Assembly.Load("Npgsql.EntityFrameworkCore.PostgreSQL");
 var attribute=assembly.GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.Design.DesignTimeProviderServicesAttribute),false).Cast<Microsoft.EntityFrameworkCore.Design.DesignTimeProviderServicesAttribute>().Single();
 ((Microsoft.EntityFrameworkCore.Design.IDesignTimeServices)Activator.CreateInstance(assembly.GetType(attribute.TypeName)!)!).ConfigureDesignTimeServices(services);
 Microsoft.EntityFrameworkCore.Design.DesignTimeServiceCollectionExtensions.AddEntityFrameworkDesignTimeServices(services);
 using var provider=Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
 var selector=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.EntityFrameworkCore.Migrations.Design.IMigrationsCodeGeneratorSelector>(provider);
 var generator=selector.Select("C#");var model=db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
 File.WriteAllText("/generated/AppDbContextModelSnapshot.cs",generator.GenerateSnapshot("MuhasebeTakip2.App.Migrations",typeof(AppDbContext),"AppDbContextModelSnapshot",model));
 File.WriteAllText("/generated/20261119160000_PreserveEmployeeSalaryIdentity.Designer.cs",generator.GenerateMetadata("MuhasebeTakip2.App.Migrations",typeof(AppDbContext),"PreserveEmployeeSalaryIdentity","20261119160000_PreserveEmployeeSalaryIdentity",model));
 Console.WriteLine("PASS generated exact EF snapshot and migration target metadata; no database connection");return;
}
var checks=0;void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks++;Console.WriteLine("PASS "+label);}
var audit=new NullAudit();var env=new FixtureEnvironment();Directory.CreateDirectory(env.WebRootPath);
T Page<T>(T page,int company=1) where T:PageModel {var http=new DefaultHttpContext{Session=new FixtureSession()};http.Session.SetInt32("FirmaId",company);page.PageContext=new PageContext{HttpContext=http};page.TempData=new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http,new FixtureTempData());return page;}
await using(var legacy=new LegacyFixtureContext(new DbContextOptionsBuilder<LegacyFixtureContext>().UseNpgsql(connection).Options))
{
 await legacy.Database.EnsureCreatedAsync();
 legacy.AddRange(Enumerable.Range(1,7).Select(id=>new Firma{Id=id,FirmaAdi="Synthetic "+id}));
 legacy.AddRange(new Calisan{Id=6,FirmaId=1,Ad="Same",AdSoyad="Same",IseGirisTarihi=DateTime.UtcNow},
 new Calisan{Id=7,FirmaId=1,Ad="Same",AdSoyad="Same",IseGirisTarihi=DateTime.UtcNow},new Calisan{Id=21,FirmaId=2,Ad="Same",AdSoyad="Same",IseGirisTarihi=DateTime.UtcNow});
 var now=DateTime.UtcNow;
 legacy.AddRange(new CalisanMaasArsiv{Id=1,FirmaId=1,CalisanId=999,ToplamMaas=3500,OdemeTarihi=now,DonemBaslangic=now,DonemBitis=now},
 new CalisanMaasArsiv{Id=3,FirmaId=1,CalisanId=2,ToplamMaas=26700,OdemeTarihi=now,DonemBaslangic=now,DonemBitis=now},
 new CalisanMaasArsiv{Id=4,FirmaId=1,CalisanId=6,ToplamMaas=100,OdemeTarihi=now,DonemBaslangic=now,DonemBitis=now});
 legacy.Add(new CalisanAvans{Id=87,FirmaId=2,CalisanId=6,Tarih=now,Tutar=1}); // Legacy FK allowed a cross-company link.
 await legacy.SaveChangesAsync();
}
async Task<string> OriginalDigest()
{await using var db=Db();return await db.Database.SqlQueryRaw<string>("""
SELECT md5(string_agg((to_jsonb(s)-'DogrulanmisCalisanId')::text,chr(10) ORDER BY s."Id")) AS "Value"
FROM public."CalisanMaasArsivleri" s WHERE s."Id" IN(1,3,4)
""").SingleAsync();}
var original=await OriginalDigest();
await using(var modelCheck=Db())Check(!modelCheck.Database.HasPendingModelChanges(),"generated EF snapshot matches application model exactly");
await using(var expectedLegacy=new LegacyFixtureContext(new DbContextOptionsBuilder<LegacyFixtureContext>().UseNpgsql(connection).Options))
await using(var candidate=Db())
{
 string Signature(Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation op)=>op switch
 {
  Microsoft.EntityFrameworkCore.Migrations.Operations.AddForeignKeyOperation x=>$"addfk|{x.Table}|{x.Name}|{string.Join(',',x.Columns)}|{x.PrincipalTable}|{string.Join(',',x.PrincipalColumns!)}|{x.OnDelete}",
  Microsoft.EntityFrameworkCore.Migrations.Operations.DropForeignKeyOperation x=>$"dropfk|{x.Table}|{x.Name}",
  Microsoft.EntityFrameworkCore.Migrations.Operations.AddUniqueConstraintOperation x=>$"unique|{x.Table}|{x.Name}|{string.Join(',',x.Columns)}",
  Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation x=>$"index|{x.Table}|{x.Name}|{string.Join(',',x.Columns)}|{x.IsUnique}",
  Microsoft.EntityFrameworkCore.Migrations.Operations.AlterColumnOperation x=>$"alter|{x.Table}|{x.Name}|{x.ClrType}|{x.IsNullable}|{x.OldColumn.IsNullable}",
  Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation x=>$"column|{x.Table}|{x.Name}|{x.ClrType}|{x.IsNullable}",
  _=>op.GetType().Name
 };
 var differ=candidate.GetService<IMigrationsModelDiffer>();
 var expected=differ.GetDifferences(expectedLegacy.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel(),candidate.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()).Select(Signature).Order().ToArray();
 var migration=new PreserveEmployeeSalaryIdentity{ActiveProvider="Npgsql.EntityFrameworkCore.PostgreSQL"};
 var actual=migration.UpOperations.Where(x=>x is not Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation).Select(Signature).Order().ToArray();
 Check(expected.SequenceEqual(actual),"migration schema operations exactly match EF employee-model delta; no implicit cascade change");
}
async Task Apply()
{
 await using var db=Db();await using var tx=await db.Database.BeginTransactionAsync();
 var migration=new PreserveEmployeeSalaryIdentity{ActiveProvider="Npgsql.EntityFrameworkCore.PostgreSQL"};
 var sql=db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations,db.Model);
 foreach(var command in sql)await db.Database.ExecuteSqlRawAsync(command.CommandText.Replace("{","{{").Replace("}","}}"));
 await tx.CommitAsync();
}
try{await Apply();throw new InvalidDataException("Legacy cross-company row accepted by migration");}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23503"){}
await using(var db=Db())
{
 Check(!await db.Database.SqlQueryRaw<bool>("""
SELECT EXISTS(SELECT FROM pg_attribute WHERE attrelid='public."CalisanMaasArsivleri"'::regclass AND attname='DogrulanmisCalisanId' AND NOT attisdropped) AS "Value"
""").SingleAsync(),"failed migration rolls back schema atomically");
 await db.Database.ExecuteSqlRawAsync("""DELETE FROM public."CalisanAvanslari" WHERE "Id"=87""");
}
await Apply();
await using(var seq=Db())await seq.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('public.\"CalisanMaasArsivleri\"','Id'),100,false)");
Check(await OriginalDigest()==original,"migration preserves original IDs, amounts, periods and dates");
await using(var db=Db())
{
 var rows=await db.CalisanMaasArsivleri.OrderBy(x=>x.Id).ToArrayAsync();
 Check(rows.Single(x=>x.Id==3).CalisanId==2 && rows.Single(x=>x.Id==3).DogrulanmisCalisanId is null && rows.Single(x=>x.Id==1).DogrulanmisCalisanId is null,"ID2/ID6 and unknown salary never automatically matched");
 Check(rows.Single(x=>x.Id==4).DogrulanmisCalisanId==6,"migration preserves only an already-valid same-ID same-company relationship");
}
async Task Reject(object entity)
{await using var db=Db();db.Add(entity);try{await db.SaveChangesAsync();throw new InvalidDataException("Invalid employee reference accepted");}catch(DbUpdateException ex) when(ex.InnerException is Npgsql.PostgresException{SqlState:"23503"}){Check(true,"database rejects cross-company or missing employee");}}
await Reject(new CalisanAvans{FirmaId=1,CalisanId=21,Tarih=DateTime.UtcNow,Tutar=1});
await Reject(new CalisanPuantaj{FirmaId=1,CalisanId=21,Tarih=DateTime.UtcNow});
await Reject(new CalisanMaasArsiv{FirmaId=1,CalisanId=21,DogrulanmisCalisanId=21,OdemeTarihi=DateTime.UtcNow,DonemBaslangic=DateTime.UtcNow,DonemBitis=DateTime.UtcNow});
await Reject(new CalisanAvans{FirmaId=1,CalisanId=99999,Tarih=DateTime.UtcNow,Tutar=1});
await using(var db=Db())
{
 var page=Page(new SalaryPage(db,audit));page.Tutar=120;Check(await page.OnPostMaasAsync(6) is RedirectToPageResult,"new salary uses active employee in current company");
 var wrong=Page(new SalaryPage(db,audit));wrong.Tutar=20;Check(await wrong.OnPostAvansAsync(21) is NotFoundResult,"other-company employee denied by salary handler");
 Check(await Page(new SalaryPage(db,audit)).OnPostAvansAsync(-6) is NotFoundResult,"nonpositive employee identity rejected");
 var attendance=Page(new AttendancePage(db,audit));attendance.CalisanId=6;attendance.Tarih=new DateTime(2026,11,16,0,0,0,DateTimeKind.Utc);
 Check(await attendance.OnPostKaydetAsync() is RedirectToPageResult && await db.CalisanPuantajlari.AnyAsync(x=>x.CalisanId==6 && x.FirmaId==1),"attendance uses same-company employee");
 await Page(new SalaryPage(db,audit)).OnPostOdendiAsync(6);
 Check(await db.CalisanMaasArsivleri.AnyAsync(x=>x.CalisanId==6 && x.DogrulanmisCalisanId==6 && x.ToplamMaas==120),"new salary archive stores original and verified identity together");
}
await using(var db=Db())
{
 await Page(new EmployeePage(db,audit)).OnPostArsivleAsync(6);
 Check(await db.Calisanlar.AnyAsync(x=>x.Id==6 && !x.AktifMi),"archive keeps employee ID and row");
 var disabled=Page(new SalaryPage(db,audit));disabled.Tutar=1;Check(await disabled.OnPostMaasAsync(6) is NotFoundResult,"inactive employee cannot receive new salary");
 var attendance=Page(new AttendancePage(db,audit));attendance.CalisanId=6;attendance.Tarih=DateTime.UtcNow;await attendance.OnPostKaydetAsync();
 Check(await db.CalisanPuantajlari.CountAsync(x=>x.CalisanId==6)==1,"inactive employee cannot receive new attendance");
 await Page(new ArchivePage(db,audit)).OnPostGeriAlAsync(6);
 Check(await db.Calisanlar.AnyAsync(x=>x.Id==6 && x.AktifMi && x.AyrilisTarihi==null) && await db.Calisanlar.CountAsync()==3,"rehire reuses exact ID without creating a person");
 var newPerson=Page(new EmployeePage(db,audit));newPerson.YeniCalisan=new Calisan{Id=6,AdSoyad="Same",IseGirisTarihi=DateTime.UtcNow};await newPerson.OnPostEkleAsync();
 Check(newPerson.YeniCalisan.Id!=6 && await db.Calisanlar.CountAsync(x=>x.FirmaId==1 && x.AdSoyad=="Same")==3,"same-name people remain distinct and posted ID cannot replace existing employee");
 try{await db.Database.ExecuteSqlRawAsync("""DELETE FROM public."Calisanlar" WHERE "Id"=6""");throw new InvalidDataException("Employee with history deleted");}catch(Npgsql.PostgresException ex) when(ex.SqlState is "23503" or "23001"){Check(true,"physical employee deletion cannot cascade away history");}
}
await using(var locker=Db())
await using(var writer=Db())
{
 var prior=await writer.CalisanAvanslari.CountAsync(x=>x.CalisanId==6);
 await using var tx=await locker.Database.BeginTransactionAsync();
 await locker.Database.ExecuteSqlRawAsync("""UPDATE public."Calisanlar" SET "AktifMi"=false WHERE "Id"=6""");
 var pending=Page(new SalaryPage(writer,audit));pending.Tutar=9;
 var blocked=pending.OnPostMaasAsync(6);await Task.Delay(150);
 Check(!blocked.IsCompleted,"salary write waits for simultaneous archive of same employee");
 await using(var independent=Db())
 {var other=Page(new SalaryPage(independent,audit));other.Tutar=13;Check(await other.OnPostAvansAsync(7).WaitAsync(TimeSpan.FromSeconds(2)) is RedirectToPageResult,"independent same-name employee is not blocked by another employee lock");}
 await tx.CommitAsync();Check(await blocked is NotFoundResult && await writer.CalisanAvanslari.CountAsync(x=>x.CalisanId==6)==prior,"concurrent archive wins without a late salary write");
 await locker.Database.ExecuteSqlRawAsync("""UPDATE public."Calisanlar" SET "AktifMi"=true WHERE "Id"=6""");
}
Check(await OriginalDigest()==original,"archive and rehire never alter existing salary values or original references");
byte[] backup;
await using(var db=Db()){backup=await new AccountingBackupService(db,env,true).ExportAsync(1);}
AccountingBackupService.Package Read(byte[] bytes){using var z=new ZipArchive(new MemoryStream(bytes));using var s=z.GetEntry("manifest.json")!.Open();return JsonSerializer.Deserialize<AccountingBackupService.Package>(s)!;}
byte[] Pack(AccountingBackupService.Package p){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){{using var s=z.CreateEntry("manifest.json").Open();JsonSerializer.Serialize(s,p,AccountingBackupService.JsonOptions);}if(p.Version>=3 && p.Warnings.Count>0){using var w=new StreamWriter(z.CreateEntry("UYARILAR.txt").Open());w.Write(string.Join("\n",p.Warnings));}}return m.ToArray();}
var package=Read(backup);Check(package.Version==5 && package.UnresolvedSalaryEmployees.Count==2,"version5 exports unresolved identities explicitly without name inference");
await using(var db=Db())
{
 await db.Database.ExecuteSqlRawAsync("""SELECT setval(pg_get_serial_sequence('public."Calisanlar"','Id'),2,false)""");
 await new AccountingBackupService(db,env,true).RestoreAsync(new MemoryStream(backup),3);
 var restored=await db.CalisanMaasArsivleri.Where(x=>x.FirmaId==3).ToArrayAsync();
 Check(restored.Single(x=>x.ToplamMaas==26700).CalisanId==2 && restored.Single(x=>x.ToplamMaas==26700).DogrulanmisCalisanId is null && await db.Calisanlar.AnyAsync(x=>x.Id==2 && x.FirmaId==3),"restore ID collision does not bind orphan salary to a newly inserted same-name employee");
 Check(restored.Single(x=>x.ToplamMaas==3500).CalisanId==999 && restored.Single(x=>x.ToplamMaas==3500).DogrulanmisCalisanId is null,"unknown original salary ID and amount survive restore");
 var mapped=restored.Single(x=>x.ToplamMaas==100);Check(mapped.CalisanId==6 && mapped.DogrulanmisCalisanId.HasValue && await db.Calisanlar.AnyAsync(x=>x.Id==mapped.DogrulanmisCalisanId && x.FirmaId==3),"valid verified link remaps by source ID while original reference remains unchanged");
 try{await new AccountingBackupService(db,env,true).RestoreAsync(new MemoryStream(backup),3);throw new InvalidDataException("Occupied target accepted");}catch(AccountingBackupService.BackupException ex) when(ex.Message.Contains("boş")){Check(true,"empty-target restore rule remains enforced");}
}
await using(var seq=Db())await seq.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('public.\"Calisanlar\"','Id'),30,false)");
await using(var db=Db())
{
 var invalid=Read(backup);invalid.Tables[nameof(CalisanMaasArsiv)].Single(r=>r["Id"].GetInt32()==4)["DogrulanmisCalisanId"]=JsonSerializer.SerializeToElement(999999);
 try{await new AccountingBackupService(db,env,true).RestoreAsync(new MemoryStream(Pack(invalid)),7);throw new InvalidDataException("Unknown verified actor imported");}
 catch(AccountingBackupService.BackupException){Check(!await db.Calisanlar.AnyAsync(x=>x.FirmaId==7),"invalid verified employee blocks restore before any target writes");}
 var reexported=await new AccountingBackupService(db,env,true).ExportAsync(3);
 Check(Read(reexported).UnresolvedSalaryEmployees.Count==2,"unresolved identity survives re-export even when original ID collides with a current employee");
 await new AccountingBackupService(db,env,true).RestoreAsync(new MemoryStream(reexported),7);
 Check(await db.CalisanMaasArsivleri.AnyAsync(x=>x.FirmaId==7 && x.ToplamMaas==26700 && x.CalisanId==2 && x.DogrulanmisCalisanId==null),"second restore preserves unresolved original ID without name/ID guessing");
}
foreach(var version in new[]{4,3,2})
{
 var old=Read(backup);old.Version=version;
 foreach(var row in old.Tables[nameof(CalisanMaasArsiv)])row.Remove("DogrulanmisCalisanId");
 if(version<4){old.Tables.Remove(nameof(KasaHesap));old.Tables.Remove(nameof(KasaTransfer));old.Tables[nameof(CalisanMaasArsiv)].RemoveAll(r=>old.UnresolvedSalaryEmployees.Any(u=>u.SalaryRecordId==r["Id"].GetInt32()));old.UnresolvedSalaryEmployees.Clear();old.Warnings.Clear();}
 await using var db=Db();var target=8-version;
 await new AccountingBackupService(db,env,true).RestoreAsync(new MemoryStream(Pack(old)),target);
 Check(await db.CalisanMaasArsivleri.AnyAsync(x=>x.FirmaId==target && x.DogrulanmisCalisanId!=null),"old ZIP version "+version+" remains supported without name matching");
}
Check(await OriginalDigest()==original,"backup and restore never change source salary records");
Console.WriteLine($"PASS employee identity: {checks} targeted checks; synthetic PostgreSQL only.");

sealed class NullAudit:IIslemGecmisiService {public Task KaydetAsync(string m,string t,string a,object? eskiDeger=null,object? yeniDeger=null,CancellationToken cancellationToken=default)=>Task.CompletedTask;}
sealed class FixtureSession:ISession {readonly Dictionary<string,byte[]> d=new();public bool IsAvailable=>true;public string Id{get;}=Guid.NewGuid().ToString();public IEnumerable<string> Keys=>d.Keys;public void Clear()=>d.Clear();public Task CommitAsync(CancellationToken c=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken c=default)=>Task.CompletedTask;public void Remove(string k)=>d.Remove(k);public void Set(string k,byte[] v)=>d[k]=v;public bool TryGetValue(string k,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? v)=>d.TryGetValue(k,out v);}
sealed class FixtureEnvironment:IWebHostEnvironment {public string ApplicationName{get;set;}="Synthetic";public string EnvironmentName{get;set;}="Testing";public string ContentRootPath{get;set;}="/work/fixture";public string WebRootPath{get;set;}="/work/fixture/wwwroot";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
sealed class LegacyFixtureContext(DbContextOptions<LegacyFixtureContext> options):AppDbContext(options)
{
 protected override void OnModelCreating(ModelBuilder b)
 {
  base.OnModelCreating(b);
  foreach(var type in new[]{typeof(CalisanAvans),typeof(CalisanPuantaj),typeof(CalisanMaasArsiv)})
  {var e=b.Model.FindEntityType(type)!;foreach(var fk in e.GetForeignKeys().Where(f=>f.PrincipalEntityType.ClrType==typeof(Calisan)).ToArray())e.RemoveForeignKey(fk);foreach(var i in e.GetIndexes().Where(i=>i.Properties.Count>1 && i.Properties.Any(p=>p.Name.Contains("CalisanId"))).ToArray())e.RemoveIndex(i);}
  b.Entity<CalisanMaasArsiv>().Ignore(x=>x.DogrulanmisCalisanId);
  var person=b.Entity<Calisan>().Metadata;foreach(var key in person.GetKeys().Where(k=>k.Properties.Count>1).ToArray())person.RemoveKey(key);
  b.Entity<Calisan>().Property(x=>x.FirmaId).IsRequired(false);b.Entity<CalisanAvans>().Property(x=>x.FirmaId).IsRequired(false);
  b.Entity<CalisanAvans>().HasOne(x=>x.Calisan).WithMany(x=>x.Avanslar).HasForeignKey(x=>x.CalisanId).OnDelete(DeleteBehavior.Cascade);
  b.Entity<CalisanPuantaj>().HasOne(x=>x.Calisan).WithMany().HasForeignKey(x=>x.CalisanId).OnDelete(DeleteBehavior.Cascade);
 }
}

sealed class FixtureTempData:Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider {public IDictionary<string,object> LoadTempData(HttpContext c)=>new Dictionary<string,object>();public void SaveTempData(HttpContext c,IDictionary<string,object> values){}}