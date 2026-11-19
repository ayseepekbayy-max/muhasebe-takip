using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

// A separate credential/person domain, never inferred from ERP user IDs or display names.
// Only catalog metadata is read. Unknown schema/relationship/trigger changes fail closed.
public static class IndependentPrivatePanelScope
{
    public static Task<bool> IsCompatibleAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>("""
            WITH expected(name,columns) AS (VALUES
              ('PrivateMessages',ARRAY['Id','ReplyToMessageId','IsReply','SenderPerson','SenderName','Content','Kind','MediaKey','MediaContentType','DurationSeconds','PhotoViewHours','PhotoAccessExpiresAtUtc','ViewedAtUtc','ExpiredAtUtc','CreatedAtUtc']),
              ('PrivatePresence',ARRAY['Id','PersonNumber','LastSeenAtUtc','MessagesClearedAtUtc','TypingUpdatedAtUtc','ShowLastSeen']),
              ('PrivateMessageHidden',ARRAY['Id','PersonNumber','PrivateMessageId','HiddenAtUtc']),
              ('PrivateMediaContents',ARRAY['MessageId','MediaKey','Payload','CreatedAtUtc','ExpiresAtUtc']),
              ('PrivateMediaQuotas',ARRAY['Id','UsedBytes'])
            ), private_tables AS (
              SELECT e.*,to_regclass(format('public.%I',e.name))::oid AS oid FROM expected e
            ), corporate_tables AS (
              SELECT c.oid FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='public' AND (c.relname IN ('Firmalar','FaturaKalemleri','CompanyStoredFiles','CompanyStoredFileReferences','CompanyFileCleanupJobs') OR
                EXISTS(SELECT FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped AND a.attname='FirmaId'))
            ), scope_tables AS (
              SELECT oid FROM private_tables UNION SELECT oid FROM corporate_tables
            )
            SELECT
              (SELECT bool_and(t.oid IS NOT NULL AND c.relkind='r' AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity AND
                t.columns @> actual.columns AND actual.columns @> t.columns)
               FROM private_tables t LEFT JOIN pg_catalog.pg_class c ON c.oid=t.oid
               CROSS JOIN LATERAL(SELECT coalesce(array_agg(a.attname::text),ARRAY[]::text[]) AS columns
                 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped) actual)
              AND EXISTS(SELECT FROM pg_catalog.pg_proc p WHERE p.oid=to_regprocedure('public.firmova_document_file_id(text)')
                AND NOT p.prosecdef AND p.provolatile='i' AND p.proconfig=ARRAY['search_path=pg_catalog, pg_temp']
                AND md5(replace(p.prosrc,chr(13),''))='dc1c855f24c71eb0ad848c66861187f0')
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_constraint f WHERE f.contype='f' AND
                ((f.conrelid IN(SELECT oid FROM private_tables) AND f.confrelid NOT IN(SELECT oid FROM private_tables)) OR
                 (f.confrelid IN(SELECT oid FROM private_tables) AND f.conrelid NOT IN(SELECT oid FROM private_tables))))
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_inherits i WHERE i.inhrelid IN(SELECT oid FROM private_tables) OR i.inhparent IN(SELECT oid FROM private_tables))
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite r WHERE r.ev_class IN(SELECT oid FROM scope_tables) AND r.ev_type<>'1' AND r.ev_enabled IN('O','A'))
              AND NOT EXISTS(
                SELECT FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
                JOIN pg_catalog.pg_proc p ON p.oid=t.tgfoid JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE t.tgrelid IN(SELECT oid FROM scope_tables) AND NOT t.tgisinternal AND t.tgenabled IN('O','A') AND NOT (
                  n.nspname='public' AND t.tgqual IS NULL AND t.tgnargs=0 AND
                  p.prolang=(SELECT oid FROM pg_catalog.pg_language WHERE lanname='plpgsql') AND (
                    (c.relname='Firmalar' AND p.proname='firmova_guard_primary_company' AND p.prosecdef AND
                     p.proconfig=ARRAY['search_path=pg_catalog, pg_temp'] AND md5(replace(p.prosrc,chr(13),''))='456f7c532eb5b9b6b6ccd78c52a40806' AND
                     ((t.tgname='firmova_guard_primary_company_row' AND t.tgtype=27) OR (t.tgname='firmova_guard_primary_company_truncate' AND t.tgtype=34))) OR
                    (c.relname IN('Firmalar','Cekler','EkDosyalar','CompanyStoredFiles','CompanyStoredFileReferences','CompanyFileCleanupJobs') AND
                     t.tgname='firmova_document_file_gate' AND t.tgtype=31 AND p.proname='firmova_guard_document_file' AND p.prosecdef AND
                     p.proconfig=ARRAY['search_path=pg_catalog, pg_temp'] AND md5(replace(p.prosrc,chr(13),''))='4b6af35d746bee3aca7569c7649bfae6') OR
                    (c.relname='PrivateMediaContents' AND NOT p.prosecdef AND (
                      (t.tgname='firmova_private_media_quota_trigger' AND t.tgtype=31 AND p.proname='firmova_private_media_quota' AND
                       p.proconfig=ARRAY['search_path=pg_catalog, public'] AND md5(replace(p.prosrc,chr(13),''))='1b0bd2183d778ba67a76a15e0e9ab883') OR
                      (t.tgname='firmova_private_media_no_truncate_trigger' AND t.tgtype=34 AND p.proname='firmova_private_media_no_truncate' AND
                       p.proconfig IS NULL AND md5(replace(p.prosrc,chr(13),''))='5115845187c5ece7ab38a6983bc3e72c')))
                  )
                )
              ) AS "Value"
            """).SingleAsync(ct);
}
