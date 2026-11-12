using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public static class IslemGecmisiPersistenceExtensions
{
    public static async Task SaveChangesWithAuditAsync(
        this AppDbContext db,
        Func<Task> auditKaydiOlustur,
        bool anaKaydiOnceKaydet,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (anaKaydiOnceKaydet)
            await db.SaveChangesAsync(cancellationToken);

        await auditKaydiOlustur();
        await db.SaveChangesAsync(cancellationToken);

        if (transaction != null) await transaction.CommitAsync(cancellationToken);
    }
}
