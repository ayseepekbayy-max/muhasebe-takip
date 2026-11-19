-- Separate approval required. Install and independently verify both database guards first.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '30s';
SET LOCAL idle_in_transaction_session_timeout = '5s';
-- Required explicit target and independently verified mapping; no business rows change.
SELECT current_database() = :'expected_database' AND EXISTS (
    SELECT FROM public.firmova_company_deletion_owner o
    JOIN public."Kullanicilar" u ON u."Id" = o.user_id
    JOIN public."Firmalar" f ON f."Id" = u."FirmaId"
    WHERE o.singleton AND o.user_id = :'main_admin_user_id'::integer
      AND u."Rol" = 'SuperAdmin' AND f."AktifMi"
) AS target_verified \gset
\if :target_verified
\else
DO $$ BEGIN RAISE EXCEPTION 'Unexpected target or admin mapping'; END $$;
\endif
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'firmova_app_runtime'
        AND NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) OR
       EXISTS (SELECT FROM pg_database WHERE datname = current_database()
           AND pg_has_role('firmova_app_runtime', datdba, 'MEMBER')) OR
       EXISTS (SELECT FROM pg_namespace WHERE nspname = 'public'
           AND pg_has_role('firmova_app_runtime', nspowner, 'MEMBER')) OR
       has_database_privilege('firmova_app_runtime', current_database(), 'CREATE') OR
       has_schema_privilege('firmova_app_runtime', 'public', 'CREATE') OR
       has_parameter_privilege('firmova_app_runtime', 'session_replication_role', 'SET') OR
       NOT EXISTS (SELECT FROM pg_proc WHERE oid = 'public.firmova_guard_primary_company()'::regprocedure
           AND prosecdef AND proconfig = ARRAY['search_path=pg_catalog, pg_temp']
           AND md5(replace(prosrc, E'\r\n', E'\n')) = '456f7c532eb5b9b6b6ccd78c52a40806'
           AND NOT pg_has_role('firmova_app_runtime', proowner, 'MEMBER')) OR
       EXISTS (SELECT FROM pg_auth_members m WHERE m.member = 'firmova_app_runtime'::regrole) OR
       EXISTS (SELECT FROM pg_class c WHERE c.oid IN ('public."Firmalar"'::regclass,
           'public.firmova_company_deletion_owner'::regclass)
           AND pg_has_role('firmova_app_runtime', c.relowner, 'MEMBER')) OR
       has_table_privilege('firmova_app_runtime', 'public.firmova_company_deletion_owner', 'INSERT,UPDATE,DELETE,TRUNCATE') OR
       has_any_column_privilege('firmova_app_runtime', 'public.firmova_company_deletion_owner', 'INSERT,UPDATE') OR
       has_column_privilege('firmova_app_runtime', 'public."Kullanicilar"', 'FirmaId', 'UPDATE') OR
       has_table_privilege('firmova_app_runtime', 'public."Firmalar"', 'TRUNCATE') OR
       has_table_privilege('firmova_app_runtime', 'public."Firmalar"', 'DELETE') OR
       has_column_privilege('firmova_app_runtime', 'public.firmova_company_deletion_owner', 'singleton', 'SELECT') OR
       has_column_privilege('firmova_app_runtime', 'public.firmova_company_deletion_owner', 'user_id', 'SELECT') OR
       EXISTS (SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
           WHERE n.nspname = 'public' AND p.proname IN ('firmova_lock_company_deletion_documents',
               'firmova_document_file_id','firmova_guard_document_file')) OR
       to_regclass('public.firmova_document_file_gates') IS NOT NULL OR
       EXISTS (SELECT FROM pg_trigger WHERE tgname='firmova_document_file_gate'
           AND tgrelid IN ('public."Firmalar"'::regclass, 'public."Cekler"'::regclass,
               'public."EkDosyalar"'::regclass, 'public."CompanyStoredFiles"'::regclass,
               'public."CompanyStoredFileReferences"'::regclass, 'public."CompanyFileCleanupJobs"'::regclass))
    THEN RAISE EXCEPTION 'Unexpected runtime privileges or existing installation'; END IF;
    IF NOT EXISTS (SELECT FROM pg_stat_ssl WHERE pid = pg_backend_pid() AND ssl) OR
       NOT has_table_privilege('firmova_app_runtime', 'public."CompanyStoredFiles"', 'SELECT') OR
       NOT has_table_privilege('firmova_app_runtime', 'public."CompanyStoredFileReferences"', 'SELECT') OR
       NOT has_table_privilege('firmova_app_runtime', 'public."CompanyFileCleanupJobs"', 'INSERT') OR
       NOT has_column_privilege('firmova_app_runtime', 'public."CompanyStoredFiles"', 'RetiredAtUtc', 'UPDATE') OR
       (SELECT count(*) FROM pg_trigger t
        WHERE tgrelid = 'public."Firmalar"'::regclass AND NOT tgisinternal AND tgenabled IN ('O', 'A')
          AND tgfoid = to_regprocedure('public.firmova_guard_primary_company()')
          AND ((tgname = 'firmova_guard_primary_company_row' AND tgtype = 27) OR
               (tgname = 'firmova_guard_primary_company_truncate' AND tgtype = 34))) <> 2
    THEN
        RAISE EXCEPTION 'Verified TLS and both company guards are required';
    END IF;
END;
$$;
GRANT SELECT ("singleton", "user_id") ON public.firmova_company_deletion_owner TO firmova_app_runtime;
GRANT DELETE ON public."Firmalar" TO firmova_app_runtime;

-- Per-file gates are enforced by triggers, not a runtime-callable table-lock helper.
\ir CompanyDocumentFileLocks.sql
DO $$
BEGIN
    IF EXISTS (SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace,
        LATERAL aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) a
        WHERE n.nspname='public' AND p.proname IN ('firmova_document_file_id','firmova_guard_document_file')
          AND a.grantee <> p.proowner) OR
       EXISTS (SELECT FROM pg_class c,
         LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) a
         WHERE c.oid='public.firmova_document_file_gates'::regclass AND a.grantee<>c.relowner)
    THEN RAISE EXCEPTION 'Unexpected document gate ACL'; END IF;
END; $$;
COMMIT;
