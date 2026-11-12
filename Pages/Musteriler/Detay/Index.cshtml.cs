using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Musteriler.Detay;

[TypeFilter(typeof(AccountingPageContextFilter))]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    private readonly IIslemGecmisiService _audit;

    public IndexModel(AppDbContext db, IIslemGecmisiService audit)
    {
        _db = db;
        _audit = audit;
    }

    [BindProperty] public int? KasaHesapId { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> HesapSecenekleri { get; set; } = new();
    public Musteri? Musteri { get; set; }
    public List<MusteriIs> Isler { get; set; } = new();

    public decimal ToplamGelir { get; set; }
    public decimal ToplamMasraf { get; set; }
    public decimal NetTutar => ToplamGelir - ToplamMasraf;

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    [BindProperty]
    public DateTime YeniIsTarih { get; set; } = DateTime.Today;

    [BindProperty]
    public string? YeniIsAdi { get; set; } = "";

    [BindProperty]
    public decimal YeniIsGelir { get; set; }

    [BindProperty]
    public int MasrafIsId { get; set; }

    [BindProperty]
    public DateTime YeniMasrafTarih { get; set; } = DateTime.Today;

    [BindProperty]
    public string? YeniMasrafAciklama { get; set; } = "";

    [BindProperty]
    public decimal YeniMasrafTutar { get; set; }

    public Task<IActionResult> OnGetAsync([FromRoute] int id) =>
        WithCustomerAsync(id, write: false, (_, _) => Task.FromResult<IActionResult>(Page()));

    public Task<IActionResult> OnPostIsEkleAsync([FromRoute] int id) =>
        WriteAsync(id, IsEkleAsync);

    public Task<IActionResult> OnPostMasrafEkleAsync([FromRoute] int id) =>
        WriteAsync(id, MasrafEkleAsync);

    public Task<IActionResult> OnPostIsSilAsync([FromRoute] int id, int isId) =>
        WriteAsync(id, (company, ct) => IsSilAsync(company, isId, ct));

    public Task<IActionResult> OnPostMasrafSilAsync([FromRoute] int id, int masrafId) =>
        WriteAsync(id, (company, ct) => MasrafSilAsync(company, masrafId, ct));

    private Task<IActionResult> WriteAsync(int id, Func<int, CancellationToken, Task<IActionResult>> operation) =>
        CustomerScope.RunWriteAsync(_db, () => WithCustomerAsync(id, write: true, operation), HttpContext.RequestAborted);

    private async Task<IActionResult> WithCustomerAsync(int id, bool write, Func<int, CancellationToken, Task<IActionResult>> operation)
    {
        var company = AccountingPageContextFilter.CompanyId(HttpContext);
        var ct = HttpContext.RequestAborted;
        if (write)
            await CustomerScope.LockCustomerAsync(_db, company, id, ct);

        if (await CustomerScope.ForeignCustomerAsync(_db, company, id, ct) ||
            !await CustomerScope.RelationsValidAsync(_db, company, id, ct))
            return StatusCode(403);

        Musteri = await _db.Musteriler.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == company && x.AktifMi, ct);
        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        Isler = await _db.MusteriIsler.AsNoTracking()
            .Where(x => x.MusteriId == id && x.FirmaId == company)
            .Include(x => x.Masraflar)
            .OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        // Validate the materialized children too, before any result renders them.
        if (Isler.Any(x => x.Masraflar.Any(masraf => masraf.FirmaId != company)))
            return StatusCode(403);

        HesapSecenekleri = await KasaHesapService.SeceneklerAsync(_db, company);
        ToplamGelir = Isler.Sum(x => x.Gelir);
        ToplamMasraf = Isler.SelectMany(x => x.Masraflar).Sum(x => x.Tutar);
        return await operation(company, ct);
    }

    private async Task<IActionResult> IsEkleAsync(int company, CancellationToken ct)
    {
        if (KasaHesapId.HasValue && await _db.KasaHesaplar.AsNoTracking()
                .AnyAsync(x => x.Id == KasaHesapId && x.FirmaId != company, ct))
            return StatusCode(403);

        var ad = (YeniIsAdi ?? "").Trim();
        if (!ModelState.IsValid || ad.Length is 0 or > 160 || YeniIsGelir < 0)
        {
            Hata = "İş adı, tarih veya gelir geçersiz.";
            return Page();
        }

        if (YeniIsGelir > 0)
        {
            await CustomerScope.LockAccountAsync(_db, company, KasaHesapId, ct);
            if (!await KasaHesapService.GecerliMiAsync(_db, company, KasaHesapId))
            {
                Hata = KasaHesapService.SecimHatasi;
                return Page();
            }
        }

        var job = new MusteriIs
        {
            FirmaId = company, MusteriId = Musteri!.Id, Ad = Musteri.AdSoyad,
            IsAdi = ad, Gelir = YeniIsGelir, Tarih = DateTime.SpecifyKind(YeniIsTarih.Date, DateTimeKind.Utc)
        };
        _db.MusteriIsler.Add(job);

        KasaHareket? movement = null;
        if (job.Gelir > 0)
        {
            movement = new KasaHareket
            {
                FirmaId = company, Tarih = job.Tarih, Tip = HareketTipi.Giris, Tutar = job.Gelir,
                KasaHesapId = KasaHesapId, Aciklama = $"Müşteri tahsilatı - {job.Ad.Trim()} - {job.IsAdi}"
            };
            _db.KasaHareketleri.Add(movement);
        }

        await _db.SaveChangesWithAuditAsync(async () =>
        {
            await _audit.KaydetAsync("Müşteriler", "İş Ekleme", $"Müşteri işi eklendi (ID: {job.Id}).",
                yeniDeger: CustomerScope.JobSnapshot(job), cancellationToken: ct);
            if (movement != null)
                await _audit.KaydetAsync("Kasa", "Ekleme", $"Müşteri işi {job.Id} için tahsilat kaydedildi (hareket ID: {movement.Id}).",
                    yeniDeger: IslemGecmisiSnapshots.KasaHareket(movement), cancellationToken: ct);
        }, anaKaydiOnceKaydet: true, cancellationToken: ct);

        return RedirectToPage(new { id = Musteri.Id });
    }

    private async Task<IActionResult> MasrafEkleAsync(int company, CancellationToken ct)
    {
        if (await ForeignJobAsync(company, MasrafIsId, ct))
            return StatusCode(403);

        var aciklama = (YeniMasrafAciklama ?? "").Trim();
        if (!ModelState.IsValid || !Isler.Any(x => x.Id == MasrafIsId) ||
            aciklama.Length > 200 || YeniMasrafTutar <= 0)
        {
            Hata = "Masraf için geçerli bir iş, tarih ve tutar girin.";
            return Page();
        }

        var expense = new MusteriMasraf
        {
            FirmaId = company, MusteriIsId = MasrafIsId, Ad = Musteri!.AdSoyad,
            Aciklama = aciklama, Tutar = YeniMasrafTutar, Tarih = DateTime.SpecifyKind(YeniMasrafTarih.Date, DateTimeKind.Utc)
        };
        _db.MusteriMasraflar.Add(expense);
        await _db.SaveChangesWithAuditAsync(
            () => _audit.KaydetAsync("Müşteriler", "Masraf Ekleme", $"Müşteri işi masrafı eklendi (ID: {expense.Id}).",
                yeniDeger: CustomerScope.ExpenseSnapshot(expense), cancellationToken: ct),
            anaKaydiOnceKaydet: true, cancellationToken: ct);

        return RedirectToPage(new { id = Musteri.Id });
    }

    private async Task<IActionResult> IsSilAsync(int company, int isId, CancellationToken ct)
    {
        if (await ForeignJobAsync(company, isId, ct))
            return StatusCode(403);

        var job = await _db.MusteriIsler.Include(x => x.Masraflar).SingleOrDefaultAsync(
            x => x.Id == isId && x.FirmaId == company && x.MusteriId == Musteri!.Id, ct);
        if (job != null)
        {
            if (job.Masraflar.Any(x => x.FirmaId != company))
                return StatusCode(403);

            var snapshot = CustomerScope.JobSnapshot(job);
            _db.MusteriMasraflar.RemoveRange(job.Masraflar);
            _db.MusteriIsler.Remove(job);
            await _db.SaveChangesWithAuditAsync(
                () => _audit.KaydetAsync("Müşteriler", "İş Silme", $"Müşteri işi ve masrafları silindi (ID: {job.Id}).",
                    eskiDeger: snapshot, cancellationToken: ct),
                anaKaydiOnceKaydet: false, cancellationToken: ct);
        }

        return RedirectToPage(new { id = Musteri!.Id });
    }

    private async Task<IActionResult> MasrafSilAsync(int company, int masrafId, CancellationToken ct)
    {
        var customerId = Musteri!.Id;
        var expenses = _db.MusteriMasraflar.Where(x => x.Id == masrafId);
        if (await expenses.AsNoTracking().AnyAsync(x => x.FirmaId != company || x.MusteriIs == null ||
                x.MusteriIs.FirmaId != company || x.MusteriIs.MusteriId != customerId, ct))
            return StatusCode(403);

        var expense = await expenses.SingleOrDefaultAsync(x => x.FirmaId == company && x.MusteriIs != null &&
            x.MusteriIs.FirmaId == company && x.MusteriIs.MusteriId == customerId, ct);
        if (expense != null)
        {
            _db.MusteriMasraflar.Remove(expense);
            await _db.SaveChangesWithAuditAsync(
                () => _audit.KaydetAsync("Müşteriler", "Masraf Silme", $"Müşteri işi masrafı silindi (ID: {expense.Id}).",
                    eskiDeger: CustomerScope.ExpenseSnapshot(expense), cancellationToken: ct),
                anaKaydiOnceKaydet: false, cancellationToken: ct);
        }

        return RedirectToPage(new { id = customerId });
    }

    private Task<bool> ForeignJobAsync(int company, int id, CancellationToken ct) =>
        _db.MusteriIsler.AsNoTracking().AnyAsync(x => x.Id == id &&
            (x.FirmaId != company || x.MusteriId != Musteri!.Id), ct);
}
