-- Run only after reverting/quiescing the integration and every document writer/worker.
-- Removes only this installer's additions. Does not undo any business deletion.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path=pg_catalog;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='30s';
SET LOCAL transaction_timeout='30s';
SET LOCAL idle_in_transaction_session_timeout='5s';
SELECT current_database()=:'expected_database' AS target_verified \gset
\if :target_verified
\else
DO $$ BEGIN RAISE EXCEPTION 'Unexpected rollback target'; END $$;
\endif
DO $$
BEGIN
 IF NOT EXISTS(SELECT FROM pg_stat_ssl WHERE pid=pg_backend_pid() AND ssl) OR
    (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
     WHERE n.nspname='public' AND p.proowner=current_user::regrole
       AND p.proconfig=ARRAY['search_path=pg_catalog, pg_temp']
       AND ((p.proname='firmova_document_file_id' AND p.oid=to_regprocedure('public.firmova_document_file_id(text)')
             AND NOT p.prosecdef AND md5(replace(p.prosrc,E'\r\n',E'\n'))='dc1c855f24c71eb0ad848c66861187f0')
         OR (p.proname='firmova_guard_document_file' AND p.oid=to_regprocedure('public.firmova_guard_document_file()')
             AND p.prosecdef AND md5(replace(p.prosrc,E'\r\n',E'\n'))='4b6af35d746bee3aca7569c7649bfae6')))<>2 OR
    (SELECT count(*) FROM pg_trigger WHERE tgname='firmova_document_file_gate' AND NOT tgisinternal
      AND tgenabled IN ('O','A') AND tgtype=31 AND tgqual IS NULL AND tgnargs=0
      AND tgfoid=to_regprocedure('public.firmova_guard_document_file()')
      AND tgrelid IN ('public."Firmalar"'::regclass,'public."Cekler"'::regclass,'public."EkDosyalar"'::regclass,
       'public."CompanyStoredFiles"'::regclass,'public."CompanyStoredFileReferences"'::regclass,'public."CompanyFileCleanupJobs"'::regclass))<>6 OR
    NOT EXISTS(SELECT FROM pg_class WHERE oid=to_regclass('public.firmova_document_file_gates') AND relowner=current_user::regrole)
 THEN RAISE EXCEPTION 'Unexpected transport, file gate definitions or owner'; END IF;
 IF EXISTS(SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
      WHERE n.nspname='public' AND p.proname IN('firmova_document_file_id','firmova_guard_document_file') AND a.grantee<>p.proowner)
 THEN RAISE EXCEPTION 'Unexpected function ACL'; END IF;
END; $$;
DROP TRIGGER firmova_document_file_gate ON public."Cekler";
DROP TRIGGER firmova_document_file_gate ON public."EkDosyalar";
DROP TRIGGER firmova_document_file_gate ON public."Firmalar";
DROP TRIGGER firmova_document_file_gate ON public."CompanyStoredFiles";
DROP TRIGGER firmova_document_file_gate ON public."CompanyStoredFileReferences";
DROP TRIGGER firmova_document_file_gate ON public."CompanyFileCleanupJobs";
DROP FUNCTION public.firmova_guard_document_file();
DROP FUNCTION public.firmova_document_file_id(text);
DROP TABLE public.firmova_document_file_gates; -- Only derived UUID conflict markers, RESTRICT.
REVOKE DELETE ON public."Firmalar" FROM firmova_app_runtime;
REVOKE SELECT(singleton,user_id) ON public.firmova_company_deletion_owner FROM firmova_app_runtime;
COMMIT;
