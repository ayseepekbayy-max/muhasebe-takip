-- Read-only, PostgreSQL deployment preflight. Does not install or repair anything.
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout='10s';
SELECT 'EMPLOYEE_COMPANY_NULLS|'||count(*) FROM public."Calisanlar" WHERE "FirmaId" IS NULL;
SELECT 'MOVEMENT_COMPANY_NULLS|'||count(*) FROM public."CalisanAvanslari" WHERE "FirmaId" IS NULL;
SELECT 'INVALID_MOVEMENT_PARENT|'||count(*) FROM public."CalisanAvanslari" a
 WHERE NOT EXISTS(SELECT FROM public."Calisanlar" c WHERE c."Id"=a."CalisanId" AND c."FirmaId"=a."FirmaId");
SELECT 'INVALID_ATTENDANCE_PARENT|'||count(*) FROM public."CalisanPuantajlari" a
 WHERE NOT EXISTS(SELECT FROM public."Calisanlar" c WHERE c."Id"=a."CalisanId" AND c."FirmaId"=a."FirmaId");
SELECT 'EXISTING_UNRESOLVED_SALARIES|'||count(*) FROM public."CalisanMaasArsivleri" s
 WHERE NOT EXISTS(SELECT FROM public."Calisanlar" c WHERE c."Id"=s."CalisanId" AND c."FirmaId"=s."FirmaId" AND c."Id">0);
SELECT 'EXPECTED_OLD_CONSTRAINTS|'||count(*) FROM pg_catalog.pg_constraint
 WHERE (conrelid='public."CalisanAvanslari"'::regclass AND conname='FK_CalisanAvanslari_Calisanlar_CalisanId')
 OR (conrelid='public."CalisanPuantajlari"'::regclass AND conname='FK_CalisanPuantajlari_Calisanlar_CalisanId');
SELECT 'VERIFIED_COLUMN_ALREADY_PRESENT|'||EXISTS(SELECT FROM pg_catalog.pg_attribute
 WHERE attrelid='public."CalisanMaasArsivleri"'::regclass AND attname='DogrulanmisCalisanId' AND NOT attisdropped)::text;
ROLLBACK;
