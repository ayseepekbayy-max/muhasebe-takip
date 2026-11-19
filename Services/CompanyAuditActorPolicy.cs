using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

// An audit actor is not the owner of the audit row or a user to add to the deletion set.
// Use the installed central-admin mapping, never a role/name supplied by the request.
internal static class CompanyAuditActorPolicy
{
    internal static Task<bool> HasUnresolvedAsync(AppDbContext db, int companyId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return db.IslemGecmisleri.AnyAsync(h => h.FirmaId == companyId && h.KullaniciId != null &&
                !db.Kullanicilar.Any(u => u.Id == h.KullaniciId && u.FirmaId == companyId), ct);

        return db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM public."IslemGecmisleri" h
                WHERE h."FirmaId" = {companyId} AND h."KullaniciId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM public."Kullanicilar" u
                      WHERE u."Id" = h."KullaniciId" AND u."FirmaId" = h."FirmaId")
                  AND NOT EXISTS (
                      SELECT 1 FROM public.firmova_company_deletion_owner o
                      JOIN public."Kullanicilar" a ON a."Id" = o.user_id
                      JOIN public."Firmalar" f ON f."Id" = a."FirmaId"
                      WHERE o.singleton AND o.user_id > 0 AND a."Id" = h."KullaniciId"
                        AND a."Rol" = 'SuperAdmin' AND a."FirmaId" > 0
                        AND a."FirmaId" <> h."FirmaId" AND f."AktifMi")
            ) AS "Value"
            """).SingleAsync(ct);
    }
}
