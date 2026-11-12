using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
namespace MuhasebeTakip2.App.Services;

public static class KasaMovementDeletion
{
    public static Task<string?> DeleteAsync(AppDbContext db, IIslemGecmisiService audit, int company, int id, CancellationToken ct = default)
        => InvoicePaymentTransaction.RunAsync(db, () => DeleteCoreAsync(db, audit, company, id, ct), ct);

    private static async Task<string?> DeleteCoreAsync(AppDbContext db, IIslemGecmisiService audit, int company, int id, CancellationToken ct)
    {
        var movement = await db.KasaHareketleri.SingleOrDefaultAsync(x => x.Id == id && x.FirmaId == company);
        if (movement == null) return "Silinecek hareket bulunamadı.";
        Fatura? invoice = null;
        object? oldInvoice = null;
        if (movement.FaturaId.HasValue)
        {
            await InvoicePaymentTransaction.LockInvoiceAsync(db, company, movement.FaturaId.Value, ct);
            invoice = await db.Faturalar.SingleOrDefaultAsync(x => x.Id == movement.FaturaId && x.FirmaId == company);
            if (invoice == null) return "Hareketin bağlı olduğu fatura bulunamadı.";
            var error = FaturaOdemeTutarliligi.Dogrula(invoice, movement, 0, movement.Tip, movement.CariKartId);
            if (error != null) return error;
            oldInvoice = IslemGecmisiSnapshots.Fatura(invoice);
            FaturaOdemeTutarliligi.Uygula(invoice, movement.Tutar, 0);
        }
        db.KasaHareketleri.Remove(movement);
        await db.SaveChangesWithAuditAsync(async () =>
        {
            await audit.KaydetAsync("Kasa", "Silme", $"Kasa hareketi silindi (ID: {movement.Id}).", eskiDeger: IslemGecmisiSnapshots.KasaHareket(movement));
            if (invoice != null)
                await audit.KaydetAsync("Faturalar", "Ödeme Düzeltme", $"{invoice.FaturaNo} faturasının ödemesi kasa hareketi silindiği için güncellendi.", oldInvoice, IslemGecmisiSnapshots.Fatura(invoice));
        }, false, ct);
        return null;
    }
}
