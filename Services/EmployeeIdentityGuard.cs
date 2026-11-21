using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

internal static class EmployeeIdentityGuard
{
    internal static async Task<Calisan?> LockActiveAsync(AppDbContext db, int company, int employee, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Employee transaction required.");
        if (company <= 0 || employee <= 0) return null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
        return db.Database.IsNpgsql()
            ? await db.Calisanlar.FromSqlInterpolated($"""
                SELECT * FROM public."Calisanlar" WHERE "Id"={employee} AND "FirmaId"={company}
                    AND "AktifMi" AND "AyrilisTarihi" IS NULL FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false)
            : await db.Calisanlar.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employee && x.FirmaId == company &&
                x.AktifMi && x.AyrilisTarihi == null, ct);
    }
}
