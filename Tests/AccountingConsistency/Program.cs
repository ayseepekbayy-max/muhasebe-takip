using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using MuhasebeTakip2.App.Pages.Kasa;
using InvoiceDetail = MuhasebeTakip2.App.Pages.Faturalar.DetayModel;
using InvoiceEdit = MuhasebeTakip2.App.Pages.Faturalar.DuzenleModel;

var checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var firma = new Firma { FirmaAdi = "Test" };
var otherFirma = new Firma { FirmaAdi = "Other" };
db.AddRange(firma, otherFirma);
await db.SaveChangesAsync();
var cari = new CariKart { FirmaId = firma.Id, Unvan = "Test Cari", Tip = CariTip.HerIkisi, AktifMi = true };
var otherCari = new CariKart { FirmaId = firma.Id, Unvan = "Other Cari", Tip = CariTip.HerIkisi, AktifMi = true };
db.AddRange(cari, otherCari); await db.SaveChangesAsync();
var account = new KasaHesap { FirmaId=firma.Id, Ad="Cash" };
db.Add(account); await db.SaveChangesAsync();
var http = new DefaultHttpContext { Session = new TestSession() };
http.Session.SetInt32("FirmaId", firma.Id);
http.Session.SetString("KullaniciAdi", "Tester");
var audit = new IslemGecmisiService(db, new HttpContextAccessor { HttpContext = http });
T Setup<T>(T page) where T : PageModel
{
    page.PageContext = new PageContext { HttpContext = http };
    page.TempData = new TempDataDictionary(http, new TestTempData());
    return page;
}
int number = 0;
async Task<Fatura> Invoice(FaturaTipi tip, decimal paid = 0)
{
    var f = new Fatura { FirmaId = firma.Id, CariKartId = cari.Id, FaturaNo = $"TEST-{++number}", Tip = tip,
        GenelToplam = 1000, OdenenToplam = paid, Durum = FaturaDurumuExtensions.OdemeDurumu(1000, paid) };
    db.Add(f); await db.SaveChangesAsync(); return f;
}
foreach (var tip in new[] { FaturaTipi.Satis, FaturaTipi.Alis })
{
    var f = await Invoice(tip, 200); // Preserve an unrelated legacy payment.
    var detail = Setup(new InvoiceDetail(db, null!, audit));
    detail.DurumForm = new() { Durum = FaturaDurumu.Odendi, KasaHesapId=account.Id, Tarih = DateTime.UtcNow.Date };
    await detail.OnPostDurumGuncelleAsync(f.Id);
    db.ChangeTracker.Clear();
    f = await db.Faturalar.SingleAsync(x => x.Id == f.Id);
    var movement = await db.KasaHareketleri.SingleAsync(x => x.FaturaId == f.Id);
    var expectedTip = tip == FaturaTipi.Satis ? HareketTipi.Giris : HareketTipi.Cikis;
    Check(f.OdenenToplam == 1000 && f.Durum == FaturaDurumu.Odendi, "Mark paid closes invoice");
    Check(movement.KasaHesapId == account.Id && movement.Tutar == 800 && movement.CariKartId == cari.Id && movement.Tip == expectedTip,
        "Only remainder is posted with correct customer and direction");
    await detail.OnPostDurumGuncelleAsync(f.Id);
    Check(await db.KasaHareketleri.CountAsync(x => x.FaturaId == f.Id) == 1, "Repeated paid action creates no duplicate");

    var edit = Setup(new HareketDuzenleModel(db, audit));
    edit.Hareket = new KasaHareket { KasaHesapId=account.Id, Id = movement.Id, CariKartId = cari.Id, Tip = expectedTip, Tutar = 300, Tarih = DateTime.UtcNow };
    await edit.OnPostAsync(); db.ChangeTracker.Clear();
    f = await db.Faturalar.SingleAsync(x => x.Id == f.Id);
    Check(f.OdenenToplam == 500 && f.KalanTutar == 500 && f.Durum == FaturaDurumu.KismenOdendi,
        "Editing movement adjusts invoice and preserves legacy amount");

    edit = Setup(new HareketDuzenleModel(db, audit));
    edit.Hareket = new KasaHareket { KasaHesapId=account.Id, Id = movement.Id, CariKartId = cari.Id, Tip = expectedTip, Tutar = 900, Tarih = DateTime.UtcNow };
    Check(await edit.OnPostAsync() is PageResult && edit.Hata.Length > 0, "Overpayment is rejected");
    db.ChangeTracker.Clear();
    Check((await db.Faturalar.SingleAsync(x => x.Id == f.Id)).OdenenToplam == 500, "Rejected edit leaves database unchanged");

    edit = Setup(new HareketDuzenleModel(db, audit));
    edit.Hareket = new KasaHareket { KasaHesapId=account.Id, Id = movement.Id, CariKartId = otherCari.Id, Tip = expectedTip, Tutar = 300, Tarih = DateTime.UtcNow };
    Check(await edit.OnPostAsync() is PageResult && edit.Hata.Length > 0, "Moving payment to another customer is rejected");

    var invoiceEdit = Setup(new InvoiceEdit(db, audit));
    await invoiceEdit.OnGetAsync(f.Id);
    invoiceEdit.Fatura.CariKartId = otherCari.Id;
    Check(await invoiceEdit.OnPostAsync() is PageResult && invoiceEdit.ModelState.Values.SelectMany(x => x.Errors).Any(x => x.ErrorMessage.StartsWith("Ödeme hareketi bulunan")), "Paid invoice cannot change customer");
    db.ChangeTracker.Clear();
    invoiceEdit = Setup(new InvoiceEdit(db, audit));
    await invoiceEdit.OnGetAsync(f.Id);
    invoiceEdit.Fatura.Tip = tip == FaturaTipi.Satis ? FaturaTipi.Alis : FaturaTipi.Satis;
    Check(await invoiceEdit.OnPostAsync() is PageResult && invoiceEdit.ModelState.Values.SelectMany(x => x.Errors).Any(x => x.ErrorMessage.StartsWith("Ödeme hareketi bulunan")), "Paid invoice cannot change direction");
    db.ChangeTracker.Clear();

    var list = Setup(new HareketlerModel(db, audit));
    await list.OnPostSilAsync(movement.Id); db.ChangeTracker.Clear();
    f = await db.Faturalar.SingleAsync(x => x.Id == f.Id);
    Check(f.OdenenToplam == 200 && f.Durum == FaturaDurumu.KismenOdendi, "Deletion removes only the linked payment");
    Check(!await db.KasaHareketleri.AnyAsync(x => x.Id == movement.Id), "Movement deleted");
}
// An unlinked cash movement remains editable without changing any invoice.
var standalone = new KasaHareket { FirmaId = firma.Id, CariKartId = cari.Id, Tip = HareketTipi.Giris, Tutar = 50 };
db.Add(standalone); await db.SaveChangesAsync();
var beforePayments = await db.Faturalar.SumAsync(x => x.OdenenToplam);
var standaloneEdit = Setup(new HareketDuzenleModel(db, audit));
standaloneEdit.Hareket = new KasaHareket { KasaHesapId=account.Id, Id = standalone.Id, CariKartId = cari.Id, Tip = HareketTipi.Giris, Tutar = 75, Tarih = DateTime.UtcNow };
await standaloneEdit.OnPostAsync();
Check(standalone.Tutar == 75 && await db.Faturalar.SumAsync(x => x.OdenenToplam) == beforePayments,
    "Unlinked cash edits leave invoices unchanged");
await Setup(new HareketlerModel(db, audit)).OnPostSilAsync(standalone.Id);
Check(await db.Faturalar.SumAsync(x => x.OdenenToplam) == beforePayments, "Unlinked cash deletion leaves invoices unchanged");
// Last payment deletion reopens the invoice.
var unpaid = await Invoice(FaturaTipi.Satis, 1000);
var last = new KasaHareket { FirmaId = firma.Id, FaturaId = unpaid.Id, CariKartId = cari.Id, Tip = HareketTipi.Giris, Tutar = 1000 };
db.Add(last); await db.SaveChangesAsync();
await Setup(new HareketlerModel(db, audit)).OnPostSilAsync(last.Id);
Check(unpaid.OdenenToplam == 0 && unpaid.Durum == FaturaDurumu.Bekliyor, "Deleting last payment reopens invoice");
// Tenant boundary and old inconsistent records are rejected without repairing unrelated data.
var old = await Invoice(FaturaTipi.Satis);
var legacy = new KasaHareket { FirmaId = firma.Id, FaturaId = old.Id, CariKartId = cari.Id, Tip = HareketTipi.Giris, Tutar = 100 };
db.Add(legacy); await db.SaveChangesAsync();
var blocked = Setup(new HareketlerModel(db, audit));
await blocked.OnPostSilAsync(legacy.Id);
Check(blocked.Hata.Length > 0 && await db.KasaHareketleri.AnyAsync(x => x.Id == legacy.Id), "Inconsistent legacy record is left untouched");
http.Session.SetInt32("FirmaId", otherFirma.Id);
var tenantEdit = Setup(new HareketDuzenleModel(db, audit));
tenantEdit.Hareket = new KasaHareket { KasaHesapId=account.Id, Id = legacy.Id, Tutar = 100, Tip = HareketTipi.Giris };
Check(await tenantEdit.OnPostAsync() is PageResult && tenantEdit.Hata.Length>0, "Another firm cannot edit movement");
http.Session.SetInt32("FirmaId", firma.Id);
db.KasaHareketleri.Add(new KasaHareket { FirmaId = firma.Id, CariKartId = otherCari.Id,
    Tip = HareketTipi.Giris, Tutar = 750 });
await db.SaveChangesAsync();
// Excel export preserves advances and agrees with the customer list.
var export = Setup(new MuhasebeTakip2.App.Pages.Araclar.ExcelIceAktarModel(db));
var file = (FileContentResult)await export.OnGetDisaAktarAsync("Cari");
using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
var ws = workbook.Worksheet(1);
var row = ws.RowsUsed().Skip(1).Single(x => x.Cell(1).GetString() == cari.Unvan);
var customers = Setup(new MuhasebeTakip2.App.Pages.CariKartlar.IndexModel(db, audit));
await customers.OnGetAsync();
Check(row.Cell(11).GetValue<decimal>() == customers.Cariler.Single(x => x.Id == cari.Id).Bakiye,
    "Excel balance equals customer screen");
var advance = ws.RowsUsed().Skip(1).Single(x => x.Cell(1).GetString() == otherCari.Unvan);
Check(advance.Cell(11).GetValue<decimal>() == -750 &&
    customers.Cariler.Single(x => x.Id == otherCari.Id).Bakiye == -750,
    "Unallocated customer advance is preserved as credit in Excel and screen");
Check(await db.IslemGecmisleri.CountAsync(x => x.Modul == "Faturalar" && x.IslemTuru == "Ödeme Düzeltme") >= 5,
    "Payment corrections are audited");
// Accounts preserve totals while transfers only move money between owned accounts.
http.Session.SetInt32("FirmaId", firma.Id);
var bank = new KasaHesap { FirmaId=firma.Id, Ad="Bank", Tur=KasaHesapTuru.Banka, AcilisBakiyesi=200 };
var foreignAccount = new KasaHesap { FirmaId=otherFirma.Id, Ad="Foreign" };
account = await db.KasaHesaplar.SingleAsync(x=>x.Id==account.Id);
account.AcilisBakiyesi=1000;
db.AddRange(bank, foreignAccount); await db.SaveChangesAsync();
var initialBalances=await KasaHesapService.BakiyelerAsync(db,firma.Id);
var initialTotal=initialBalances.Sum(x=>x.Bakiye);
var initialCashCount=await db.KasaHareketleri.CountAsync();
var initialCashSum=await db.KasaHareketleri.SumAsync(x=>x.Tutar);
var initialInvoiceSum=await db.Faturalar.SumAsync(x=>x.OdenenToplam);
Check(initialBalances.Count==2 && initialBalances.Single(x=>x.Hesap.Id==bank.Id).Bakiye==200, "Opening balances and tenant isolation");
var transferPage=Setup(new HesaplarModel(db,audit));
transferPage.Transfer=new() { KaynakHesapId=account.Id,HedefHesapId=bank.Id,Tutar=250,Tarih=DateTime.UtcNow.Date };
Check(await transferPage.OnPostTransferAsync() is RedirectToPageResult, "Valid transfer accepted");
var transferred=await KasaHesapService.BakiyelerAsync(db,firma.Id);
Check(transferred.Single(x=>x.Hesap.Id==bank.Id).Bakiye==450 && transferred.Single(x=>x.Hesap.Id==account.Id).Bakiye==initialBalances.Single(x=>x.Hesap.Id==account.Id).Bakiye-250, "Transfer changes both accounts by equal opposite amounts");
Check(transferred.Sum(x=>x.Bakiye)==initialTotal && await db.KasaHareketleri.CountAsync()==initialCashCount && await db.KasaHareketleri.SumAsync(x=>x.Tutar)==initialCashSum && await db.Faturalar.SumAsync(x=>x.OdenenToplam)==initialInvoiceSum, "Transfer changes neither total nor income/expense/customer payments");
foreach(var input in new[] {
    new HesaplarModel.TransferForm { KaynakHesapId=account.Id,HedefHesapId=account.Id,Tutar=1 },
    new HesaplarModel.TransferForm { KaynakHesapId=account.Id,HedefHesapId=foreignAccount.Id,Tutar=1 },
    new HesaplarModel.TransferForm { KaynakHesapId=account.Id,HedefHesapId=bank.Id,Tutar=-1 },
    new HesaplarModel.TransferForm { KaynakHesapId=account.Id,HedefHesapId=bank.Id,Tutar=1.001m } })
{
    var invalid=Setup(new HesaplarModel(db,audit)); invalid.Transfer=input;
    Check(await invalid.OnPostTransferAsync() is PageResult && invalid.Hata.Length>0 && await db.KasaTransferler.CountAsync()==1, "Invalid transfer rejected without mutation");
}
await Setup(new HesaplarModel(db,audit)).OnPostAktiflikAsync(bank.Id);
Check(!await KasaHesapService.GecerliMiAsync(db,firma.Id,bank.Id) && (await KasaHesapService.BakiyelerAsync(db,firma.Id)).Single(x=>x.Hesap.Id==bank.Id).Bakiye==450,"Inactive accounts retain balances but reject new entries");
var inactive=Setup(new HesaplarModel(db,audit)); inactive.Transfer=new(){KaynakHesapId=account.Id,HedefHesapId=bank.Id,Tutar=1};
Check(await inactive.OnPostTransferAsync() is PageResult && await db.KasaTransferler.CountAsync()==1,"Inactive transfer endpoint rejected");
var transferId=await db.KasaTransferler.Select(x=>x.Id).SingleAsync();
http.Session.SetInt32("FirmaId",otherFirma.Id);
Check(await Setup(new HesaplarModel(db,audit)).OnPostTransferSilAsync(transferId) is NotFoundResult,"Foreign firm cannot delete transfer");
http.Session.SetInt32("FirmaId",firma.Id);
await Setup(new HesaplarModel(db,audit)).OnPostTransferSilAsync(transferId);
Check(!await db.KasaTransferler.AnyAsync() && (await KasaHesapService.BakiyelerAsync(db,firma.Id)).Single(x=>x.Hesap.Id==bank.Id).Bakiye==200,"Transfer removal reverses both account effects");
await Setup(new HesaplarModel(db,audit)).OnPostAktiflikAsync(bank.Id);
var failing=Setup(new HesaplarModel(db,new FailingAudit())); failing.Transfer=new(){KaynakHesapId=account.Id,HedefHesapId=bank.Id,Tutar=10};
try { await failing.OnPostTransferAsync(); throw new Exception("Expected audit failure"); } catch(InvalidOperationException) { }
db.ChangeTracker.Clear();
Check(!await db.KasaTransferler.AnyAsync() && (await KasaHesapService.BakiyelerAsync(db,firma.Id)).Sum(x=>x.Bakiye)==initialTotal,"Audit failure rolls back transfer atomically");
var invalidMovement=new KasaHareket{FirmaId=firma.Id,KasaHesapId=foreignAccount.Id,Tip=HareketTipi.Giris,Tutar=1};
db.Add(invalidMovement);
try { await db.SaveChangesAsync(); throw new Exception("Expected tenant FK failure"); } catch(DbUpdateException) { db.ChangeTracker.Clear(); checks++; }
var duplicate=Setup(new HesaplarModel(db,audit)); duplicate.Yeni=new(){Ad="bank"};
Check(await duplicate.OnPostEkleAsync() is PageResult && duplicate.Hata.Length>0,"Duplicate account names rejected case-insensitively");
var missing=Setup(new HareketEkleModel(db,audit)); missing.TutarText="10,00";
Check(await missing.OnPostAsync() is PageResult && await db.KasaHareketleri.CountAsync()==initialCashCount,"Cash entry requires account");
var receipt=Setup(new HareketEkleModel(db,audit)); receipt.TutarText="10,00"; receipt.Hareket=new(){KasaHesapId=bank.Id,Tip=HareketTipi.Giris,Tarih=DateTime.UtcNow};
await receipt.OnPostAsync();
Check(await db.KasaHareketleri.AnyAsync(x=>x.KasaHesapId==bank.Id && x.Tutar==10) && (await KasaHesapService.BakiyelerAsync(db,firma.Id)).Single(x=>x.Hesap.Id==bank.Id).Bakiye==210,"Receipt posts to chosen account");
await using(var pg=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused").Options))
    Check(!pg.Database.HasPendingModelChanges(),"PostgreSQL migration snapshot matches application model");

Console.WriteLine($"PASS: {checks} accounting consistency checks.");

sealed class TestSession : ISession
{
    readonly Dictionary<string, byte[]> values = new();
    public bool IsAvailable => true;
    public string Id => "test";
    public IEnumerable<string> Keys => values.Keys;
    public void Clear() => values.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => values.Remove(key);
    public void Set(string key, byte[] value) => values[key] = value;
    public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
}
sealed class TestTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

sealed class FailingAudit : IIslemGecmisiService
{
    public Task KaydetAsync(string modul,string islemTuru,string aciklama,object? eskiDeger=null,object? yeniDeger=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Injected audit failure");
}
