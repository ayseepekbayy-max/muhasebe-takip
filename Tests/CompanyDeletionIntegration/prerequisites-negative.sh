set -eu
# Called only inside the synthetic network-none fixture after install/rollback proof.
assert_absent() {
 psql -X -q -v ON_ERROR_STOP=1 <<'SQL'
DO $$ BEGIN
 IF to_regprocedure('public.firmova_lock_company_deletion_documents()') IS NOT NULL OR
    to_regprocedure('public.firmova_guard_document_file()') IS NOT NULL OR
    to_regclass('public.firmova_document_file_gates') IS NOT NULL OR
    has_table_privilege('firmova_app_runtime','public."Firmalar"','DELETE') OR
    has_column_privilege('firmova_app_runtime','public.firmova_company_deletion_owner','user_id','SELECT')
 THEN RAISE EXCEPTION 'Failed installer changed prerequisites'; END IF;
END $$;
SQL
}
reject_install() {
 if psql -X -q -v expected_database="$1" -v main_admin_user_id=101 -f /sql/GrantCompanyDeletionRuntime.sql >/work/rejected.log 2>&1; then
  echo "FAIL unsafe installer accepted: $1"; cat /work/rejected.log; exit 1
 fi
 assert_absent
}
reject_install wrong_synthetic_target
psql -X -q -c 'ALTER TABLE public."Firmalar" DISABLE TRIGGER firmova_guard_primary_company_row'
reject_install firmova_stage10_synthetic
psql -X -q -c 'ALTER TABLE public."Firmalar" ENABLE TRIGGER firmova_guard_primary_company_row'
psql -X -q -c 'ALTER ROLE firmova_app_runtime CREATEROLE'
reject_install firmova_stage10_synthetic
psql -X -q -c 'ALTER ROLE firmova_app_runtime NOCREATEROLE'
psql -X -q -c 'GRANT UPDATE (user_id) ON public.firmova_company_deletion_owner TO firmova_app_runtime'
reject_install firmova_stage10_synthetic
psql -X -q -c 'REVOKE UPDATE (user_id) ON public.firmova_company_deletion_owner FROM firmova_app_runtime'
psql -X -q -c 'ALTER DEFAULT PRIVILEGES GRANT EXECUTE ON FUNCTIONS TO synthetic_observer'
reject_install firmova_stage10_synthetic
psql -X -q -c 'ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM synthetic_observer'
echo 'PASS wrong target, disabled guard, elevated role, mapping write and unexpected default ACL reject atomically'
# A non-TLS session is rejected even though it is isolated and authenticated.
if PGSSLMODE=disable psql -X -q -v expected_database=firmova_stage10_synthetic -v main_admin_user_id=101 -f /sql/GrantCompanyDeletionRuntime.sql >/work/rejected.log 2>&1; then
 echo 'FAIL non-TLS accepted'; exit 1
fi
assert_absent
echo 'PASS non-TLS installer rejected without privilege changes'
