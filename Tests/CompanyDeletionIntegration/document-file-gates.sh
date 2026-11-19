set -eu
test "$PGDATABASE" = firmova_stage10_synthetic
psql -X -q -v ON_ERROR_STOP=1 -v expected_database=firmova_stage10_synthetic -v main_admin_user_id=101 -f /sql/GrantCompanyDeletionRuntime.sql
psql -X -q -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO public."CompanyStoredFiles"("Id","OwnerCompanyId","Length","Sha256") VALUES
 ('11111111-1111-1111-1111-111111111111',73,1,repeat('A',64)),
 ('22222222-2222-2222-2222-222222222222',74,1,repeat('B',64));
DO $$ BEGIN
 IF to_regprocedure('public.firmova_lock_company_deletion_documents()') IS NOT NULL OR
    has_function_privilege('firmova_app_runtime','public.firmova_guard_document_file()','EXECUTE') OR
    has_function_privilege('firmova_app_runtime','public.firmova_document_file_id(text)','EXECUTE') OR
    has_table_privilege('firmova_app_runtime','public.firmova_document_file_gates','SELECT,INSERT,UPDATE,DELETE')
 THEN RAISE EXCEPTION 'Excess document gate privilege'; END IF;
END $$;
DO $$ BEGIN
 BEGIN
  PERFORM public.firmova_document_file_id(U&'/uploads/.firmova-private/objects/ce\212Aler/11111111-1111-1111-1111-111111111111.jpeg');
  RAISE EXCEPTION 'Unicode alias accepted without a provable identity';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF public.firmova_document_file_id('/UPLOADS/.FIRMOVA-PRIVATE/objects/cekler/11111111-1111-1111-1111-111111111111%2Ejpeg?x=1')<>'11111111-1111-1111-1111-111111111111'::uuid
 THEN RAISE EXCEPTION 'ASCII or encoded alias mismatch'; END IF;
END $$;
SQL
psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 <<'SQL'
BEGIN ISOLATION LEVEL SERIALIZABLE;
UPDATE public."Firmalar" SET "LogoYolu"='/uploads/.firmova-private/recovery/33333333333333333333333333333333/44444444444444444444444444444444.pdf' WHERE "Id"=74;
ROLLBACK;
SQL
PGAPPNAME=synthetic_file_holder psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 >/work/file-holder.log 2>&1 <<'SQL' &
BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('11111111-1111-1111-1111-111111111111',471006));
SELECT pg_sleep(3);
ROLLBACK;
SQL
holder=$!
for attempt in $(seq 1 100); do
 if test "$(psql -X -Atq -c "SELECT EXISTS(SELECT FROM pg_locks WHERE locktype='advisory' AND granted AND pid<>pg_backend_pid())")" = t; then break; fi
 sleep 0.02
done
# Different company, different file must not wait for A.
psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
SET LOCAL lock_timeout='150ms';
UPDATE public."Firmalar" SET "LogoYolu"='/uploads/.firmova-private/objects/cekler/22222222-2222-2222-2222-222222222222.jpeg' WHERE "Id"=74;
ROLLBACK;
SQL
# An encoded/case/query alias of A must use the SAME gate even from another firm.
if psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 >/work/same-file.log 2>&1 <<'SQL'
BEGIN;
SET LOCAL lock_timeout='150ms';
UPDATE public."Firmalar" SET "LogoYolu"='/UPLOADS/.FIRMOVA-PRIVATE/objects/cekler/11111111-1111-1111-1111-111111111111%2Ejpeg?x=1' WHERE "Id"=74;
ROLLBACK;
SQL
then echo 'FAIL same file escaped its gate'; exit 1; fi
grep -q 'lock timeout' /work/same-file.log
wait "$holder"
echo 'PASS separate-company separate-file writes proceed; encoded same-file alias waits'

# A stale repeatable-read writer must not resurrect a reference after retirement.
PGAPPNAME=synthetic_rr psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 >/work/stale-reference.log 2>&1 <<'SQL' &
\set VERBOSITY verbose
BEGIN ISOLATION LEVEL REPEATABLE READ;
SELECT count(*) FROM public."CompanyStoredFiles";
SELECT pg_sleep(2);
UPDATE public."Firmalar" SET "LogoYolu"='/uploads/.firmova-private/objects/cekler/22222222-2222-2222-2222-222222222222.jpeg' WHERE "Id"=74;
COMMIT;
SQL
stale=$!
ready=false
for attempt in $(seq 1 100); do
 if test "$(psql -X -Atq -c "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name='synthetic_rr' AND wait_event='PgSleep')")" = t; then ready=true; break; fi
 sleep 0.02
done
test "$ready" = true
psql -X -q -v ON_ERROR_STOP=1 <<'SQL'
UPDATE public."CompanyStoredFiles" SET "RetiredAtUtc"=now() WHERE "Id"='22222222-2222-2222-2222-222222222222';
SQL
if wait "$stale"; then echo 'FAIL stale reference writer committed'; exit 1; fi
grep -q '40001' /work/stale-reference.log
echo 'PASS stale repeatable-read reference writer rejected by persistent conflict row'

if psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 >/work/retired-reference.log 2>&1 <<'SQL'
\set VERBOSITY verbose
UPDATE public."Firmalar" SET "LogoYolu"='/uploads/.firmova-private/objects/cekler/22222222-2222-2222-2222-222222222222%2Ejpeg' WHERE "Id"=74;
SQL
then echo 'FAIL retired reference admitted'; exit 1; fi
grep -q '23514' /work/retired-reference.log
psql -X -q -U firmova_app_runtime -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
DO $$ DECLARE id integer; BEGIN
 BEGIN
  UPDATE public."CompanyStoredFiles" SET "RetiredAtUtc"=NULL WHERE "Id"='22222222-2222-2222-2222-222222222222';
  RAISE EXCEPTION 'Runtime reactivated a retired file';
 EXCEPTION WHEN check_violation THEN NULL; END;
 FOREACH id IN ARRAY ARRAY[1,72] LOOP
  BEGIN DELETE FROM public."Firmalar" WHERE "Id"=id; RAISE EXCEPTION 'Protected company deleted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 IF (SELECT count(*) FROM public."Firmalar" WHERE "Id" IN(1,72,73,74) AND "LogoYolu"='')<>4
 THEN RAISE EXCEPTION 'Another company changed'; END IF;
END $$;
ROLLBACK;
SQL
echo 'PASS retired references rejected; primary/admin and other companies preserved'
psql -X -q -v ON_ERROR_STOP=1 -v expected_database=firmova_stage10_synthetic -f /sql/RollbackCompanyDeletionRuntime.sql
psql -X -q -v ON_ERROR_STOP=1 <<'SQL'
DO $$ BEGIN
 IF to_regclass('public.firmova_document_file_gates') IS NOT NULL OR
    to_regprocedure('public.firmova_guard_document_file()') IS NOT NULL OR
    (SELECT count(*) FROM public."Firmalar")<>4 OR
    (SELECT count(*) FROM pg_trigger WHERE tgname LIKE 'firmova_guard_primary_company_%' AND tgenabled='O')<>2 OR
    (SELECT count(*) FROM public."CompanyStoredFiles")<>2
 THEN RAISE EXCEPTION 'Rollback damaged business rows or primary guards'; END IF;
END $$;
SQL
echo 'PASS separate rollback removes only file gates and new grants; business rows/primary guards retained'
