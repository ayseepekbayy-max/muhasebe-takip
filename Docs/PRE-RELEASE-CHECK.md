# PostgreSQL media release review — updated 26 October 2026

Status: NOT approved for production execution. No live connection, migration, commit,
push, deployment, restart or configuration change was performed in this review.

## Baseline and scope

GitHub main was read through the GitHub connector: 5a0dd28738412c871c0043aa4e1b13fb84d31a4f
(Restrict demo access to Development environment). Local branch:
feature/postgresql-private-media, based on that commit; media changes are uncommitted.
Login pages, demo seeder, private authentication and password helper match main.
Production/Staging/Review demo POST returns 403 before any database command.
No executable R2/Cloudflare/S3 integration or AWS package was found in the release scope.

Migration order: 20261020103737_AddCashBankAccounts ->
20261024104204_AddPostgresPrivateMedia. No intervening PostgreSQL migration.
The new migration creates two tables, indexes, two functions and two triggers,
seeds the singleton quota, and inserts its history row. It does not migrate old files.

## Current tests

PostgreSQL synthetic harness now uses the existing official postgres:18 image and
tmpfs at /var/lib/postgresql (the PostgreSQL 18 data location). No live data is used.

- PostgreSQL media/migration: 144 passed on PostgreSQL 18.6.
- Accounting ZIP backup/restore: 118 passed on PostgreSQL 18.6.
- Demo/authentication HTTP: 83 passed with temporary synthetic SQLite databases.
- JavaScript viewer/rendering checks: passed.
- Read-only migration checker self-test: passed; no network connection.
- PrivatePhotos: 62/62 PASS in isolated Linux/.NET 10 with PostgreSQL 18.6.
- AccountingConsistency: 48/48 PASS in a separate isolated Linux/.NET 10 fixture
  with PostgreSQL 18.6. Windows Application Control was not changed or bypassed.
- CS8629 at the customer-debt endpoint was fixed by explicitly rejecting a missing
  authenticated tenant before passing the non-null ID. A fresh offline Linux build
  with CS8629 treated as error passed with zero compiler warnings/errors. Nine tests
  of the actual compiled tenant helper passed without database/network access.
  The already-passing media/accounting suites were not repeated for this local guard.
- Known existing NU1903 warning: SQLitePCLRaw.lib.e_sqlite3 2.1.11.

No blocked test suites remain. Current live checks described below are still open.

## Configuration proposal (not applied)

ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
Database__Provider=PostgreSql
Database__AutoMigrate=false
PrivateMedia__BlockNewUploads=true
PrivateMedia__DatabaseReserveBytes=268435456

PrivateMedia__MaxDatabaseBytes=1000000000 (using the previously confirmed 1 GB plan).
Do not copy the synthetic 10 GiB test ceiling. The reported 10.34% disk usage is
user-supplied and was not re-read from Render. At a decimal 1 GB ceiling and 256 MiB
reserve, the current pg_database_size must not exceed 312134144 bytes for uploads
to pass the conservative 400 MiB headroom guard. This does not measure WAL or free
filesystem space. Configure monitoring separately; quotas do not reserve disk.

Keep the existing PostgreSQL runtime secret in Render only. It must use certificate
verification (SSL Mode=VerifyFull), Include Error Detail=false and Log Parameters=false.
Do not paste the connection string into source, this report or SQL. App startup
validates required fields but does not force VerifyFull: secret validation remains open.
No new R2 credentials/settings are required. No configurable TTL/quota setting exists:
24 hours / 104857600 logical bytes are enforced in source and database constraints.

In Render, Save only prepares variables without deployment; they take effect on the
next deployment. Never deploy this code with AutoMigrate=true. A later change of
BlockNewUploads requires the hosting configuration/deployment lifecycle; it is not
an instantly effective remote kill switch in the currently running process.

## Controlled sequence, after separate human authorization

1. The two formerly blocked test suites are complete. Review the existing dependency
   advisory. Reconfirm current size/history, TLS and role grants before execution.
2. Settle legacy file-based media handling before the first new deployment. Confirm
   a rollback artifact for main and the current backup's age/recovery window. If new
   accounting records were written since the tested backup, obtain an authorized
   fresh full backup/recovery point before the change window. Accounting ZIP excludes chat.
3. Prepare explicit file selection below; review PR. Disable Render automatic
   deployment before any approved push/merge so merging cannot deploy early.
4. Prepare the proposed variables with uploads blocked, using Save only. Confirm the
   old running application also has AutoMigrate=false; do not assume a saved value
   has changed the currently running process.
5. Apply ONLY the reviewed baseline-to-target SQL in a separate controlled session
   with PostgreSQL 18 psql, ON_ERROR_STOP and fixed public search_path, short lock
   timeout and bounded statement timeout. Review SQL is generated offline from EF;
   it already contains START TRANSACTION/COMMIT. Do not wrap it in another transaction
   or run the entire historical chain. Preflight must assert the baseline is applied,
   target absent and new object names free; abort if any assertion differs.
6. Validate history, two tables, unique/index/FK/check constraints, quota Id=1/UsedBytes=0,
   two functions and enabled triggers. Grant/verify runtime permissions before deploying.
7. Deploy only the approved artifact with uploads blocked. Verify production demo POST
   denial, accounting/text availability, no missing-table errors and cleanup health.
   Do not use a media upload as a smoke test while the gate is closed.
8. Review disk headroom and permissions, then obtain separate authorization to enable
   uploads. Validate first new media only after that approval and establish continuing
   backup/restore and monitoring for the new media-bearing database.

Migration role: separate from runtime, CONNECT to this database; USAGE/CREATE on
public, USAGE on plpgsql; SELECT on history and INSERT into history; REFERENCES on
PrivateMessages(Id). It owns its newly created tables/functions and can therefore
create indexes/triggers and ALTER their storage options. Trigger creation also needs
EXECUTE on the trigger function. Superuser is not required. This list is a proposal;
the live grants/default privileges/role memberships have NOT been inspected.

Runtime role: retain existing application permissions; add schema USAGE, SELECT/INSERT/
DELETE on PrivateMediaContents, SELECT and UPDATE(UsedBytes) on PrivateMediaQuotas,
and existing message SELECT/INSERT/UPDATE/DELETE/sequence permissions. The functions
are SECURITY INVOKER: runtime needs quota UPDATE and metadata SELECT. Do not grant
runtime CREATE, ownership, TRUNCATE, replication/session trigger bypass or migration
history writes. Quota enforcement is not a security boundary against a credential
holder allowed to directly alter UsedBytes; verify the intended trust model. Restricted
runtime/migration roles have not yet been exercised; current integration tests use
the synthetic PostgreSQL owner. Never silently change functions to SECURITY DEFINER.

## Retention and rollback boundaries

100 MiB is total payload bytes, not database size; a row-locked trigger serializes
quota updates, including direct INSERT and FK cascade deletion. Checks enforce exact
24-hour payload expiry, valid keys and payload size; existing data is still readable
when the upload gate is closed. HTTP expiry checks do not wait for physical deletion.
Cleanup runs at startup and every 30 seconds, up to 100 messages per batch; missed
work/backlogs can delay physical deletion. Backups/WAL may retain bytes longer.

BlockNewUploads ONLY blocks new uploads. It does not stop reads, deletes or cleanup.
Cleanup can mark old file-based messages expired and clear their metadata. Old
payloads are not imported, and new code has no file/R2 fallback. Consequently a
reversible first deployment cannot be assumed merely because no new media was sent.
Resolve legacy-media handling and accept retention behavior before deployment.

If migration succeeds but deployment fails before any media activity, retain the
additive schema/history and roll the application back to the reviewed main artifact;
do not invoke Down. That is not byte-for-byte rollback of message metadata or ephemeral
files. After any new bytea media exists, old code cannot read it. Prefer a forward fix
or a compatible rollback build; retain the database. A full pre-change restore would
also discard later accounting/chat writes and must not be used as casual rollback.

## Explicit release file manifest

These 30 files are candidates for the approved feature-branch GitHub change. The
original 29-file manifest is extended only by ReleaseChecks.cs, required by the
subsequently authorized READ ONLY checker extension. At the time of this report's
pre-commit update, none was staged,
committed or pushed here. Paths below are repository-relative.

```text
Data/AppDbContext.cs
Migrations/AppDbContextModelSnapshot.cs
Migrations/20261024104204_AddPostgresPrivateMedia.cs
Migrations/20261024104204_AddPostgresPrivateMedia.Designer.cs
Models/PrivateMessage.cs
Models/PostgresPrivateMedia.cs
Pages/Panel.cshtml
Pages/Panel.cshtml.cs
Program.cs
Services/PrivateMediaCleanupService.cs
Services/PrivateMediaStore.cs
Tests/AccountingBackup/AccountingBackup.csproj
Tests/AccountingBackup/Program.cs
Tests/AccountingConsistency/AccountingConsistency.csproj
Tests/AccountingConsistency/Program.cs
Tests/PrivatePhotos/PrivatePhotos.csproj
Tests/PrivatePhotos/Program.cs
Tests/PrivatePhotos/photo-viewer.cjs
Tests/AiTenantGuard/AiTenantGuard.csproj
Tests/AiTenantGuard/Program.cs
Tests/PostgresMedia/PostgresMedia.csproj
Tests/PostgresMedia/Program.cs
Tests/PostgresMedia/TemporaryPostgres.cs
Tests/ReadOnlyMigrationCheck/ReadOnlyMigrationCheck.csproj
Tests/ReadOnlyMigrationCheck/Program.cs
Tests/ReadOnlyMigrationCheck/ReleaseChecks.cs
Tests/ReadOnlyMigrationCheck/Invoke-ReadOnlyCheck.ps1
Docs/POSTGRES-PRIVATE-MEDIA.md
Docs/AddPostgresPrivateMedia.review.sql
Docs/PRE-RELEASE-CHECK.md
```

Do not include desktop exports, backup hashes/contents, local restore scripts,
credentials, databases, bin/obj or the R2 working directory. Demo security files
already exist on main and need no replacement. Avoid git add . across workspaces.

## References

- https://www.postgresql.org/docs/18/ddl-priv.html
- https://www.postgresql.org/docs/18/sql-createfunction.html
- https://www.postgresql.org/docs/18/sql-createtrigger.html
- https://render.com/docs/configure-environment-variables

## Final operational checks — 26 October 2026

GitHub main was checked again and remains 5a0dd28738412c871c0043aa4e1b13fb84d31a4f.
The future release branch is feature/postgresql-private-media. No release commit
exists yet; its hash must not be invented or confused with the baseline main hash.
Proposed future commit title: Add PostgreSQL private media with bounded retention.

The user's SecureString exists only in their PowerShell session, not the agent's
process. No credential was requested in chat or discovered from files/environment.
Consequently there was NO new live connection, size/history read, TLS handshake or
role inspection in this final review. The last known CashBankAccounts-present /
PostgresPrivateMedia-absent history is historical evidence, not a current measurement.

The previous 10.34% disk usage is potentially stale. If applied to the decimal 1 GB
capacity it is 103400000 bytes, but Render physical disk usage is NOT identical to
pg_database_size. With a 1000000000-byte ceiling, the code requires:
current_database_bytes + 419430400 + 268435456 <= 1000000000.
The upload threshold is therefore 312134144 bytes (about 297.67 MiB). Both proposed
configuration values fit the code's long-valued settings. They do not reserve disk.

Suggested operational thresholds, not provider defaults or configured alarms:
- Render disk >=25%: warning; check trend and WAL/TOAST/bloat.
- Render disk >=30%, or pg_database_size >=300000000: keep/turn uploads off pending
  review. A setting change must actually reach running instances; Save only is insufficient.
- Render disk >=50%: critical, investigate immediately.
- Render disk >=70%: emergency; avoid new media, restore work and heavy operations
  on this instance; preserve backups and assess capacity without deleting business data.
- Also warn if projected headroom exhaustion is under 24 hours, even below thresholds.
Observe disk metrics at five-minute intervals around rollout and at least hourly
until growth stabilizes; daily review afterwards. Use the existing Render Metrics
page (disk usage/capacity) and local read-only metadata checks. No paid monitoring,
autoscaling, messaging integration or scheduled task was enabled. No automatic disk
alert/kill switch exists in the current code. pg_database_size cannot observe all WAL.

TLS: the read-only tool forces VerifyFull; production startup only validates required
connection fields and does not force that mode. VerifyFull checks the trust chain and
hostname; an untrusted CA or wrong hostname must fail. No permissive certificate
callback was found. If using an internal self-signed endpoint, provide its properly
trusted CA and matching hostname or use the supported external hostname; never
fall back to Require/Prefer or trust-all. Match verification in the Linux runtime,
not only the user's Windows trust store. This was code/documentation review, not a
new production certificate test. GSS Encryption Mode=Disable can explicitly select
the TLS path if strict TLS rather than GSS negotiation is required.

Role requirements above were reviewed against the SQL/source, not verified live.
Before migration, read-only has_*_privilege and pg_roles checks must establish actual
migrator/runtime identity, membership and grants. After creation, new-object grants
must be checked too; they cannot be proven before the objects exist. No grants were
changed. A separate runtime with no DDL/superuser is preferable; the invoker quota
function still requires SELECT/UPDATE on its quota row. Current synthetic tests do
not prove that a restricted production runtime has those privileges.

Legacy media findings are unchanged: no importer, no file fallback, no inspection
of real media, and no proof that old ephemeral bytes still exist. Old media cannot
be promised to remain readable after rollout. The upload gate does not suspend
cleanup or preserve old metadata. Obtain an explicit legacy-media policy before
deployment: either accept expiration/unavailability or design a separately approved
preservation path. No silent file migration is part of this release.

Release decision: local implementation/tests ready for review; production execution
is still conditional on current metadata/TLS/grant checks and acceptance of legacy
media/rollback behavior. Apply only the reviewed SQL with controlled roles after
fresh preflight and backup-age review, verify the additive schema, then deploy the
approved artifact with uploads blocked. Enable uploads only after separate signoff.
Do not run Down or restore an old full backup over newer accounting writes as a
routine rollback. Keep the additive schema if reverting the application artifact.

Additional references:
- https://www.npgsql.org/doc/security.html
- https://render.com/docs/service-metrics

## Authorized feature-branch send

The user explicitly accepted that legacy photos/audio will not be migrated. This
resolves the legacy-media decision gate; it does not promise readability of old
ephemeral files. Newly sent payloads use PostgreSQL's permanent tables, subject to
the existing viewing rules and 24-hour cleanup, not indefinite retention.

GitHub main was rechecked before sending: 5a0dd28738412c871c0043aa4e1b13fb84d31a4f.
The Render muhasebe-takip service's Settings page was inspected read-only: Auto-Deploy
is Off and its configured branch is main. No Render setting was changed. Only
feature/postgresql-private-media is authorized for commit/push; no PR merge or deploy.

The expanded READ ONLY checker passed its offline self-test and 53 dedicated
synthetic PostgreSQL 18.6 checks (19 absent-table, 34 present-table), including
role grants and rejection of a non-TLS fixture by VerifyFull. No production query
was executed during preparation. New-table runtime permissions remain explicitly
unverified until those objects exist. Old media content and inventory were not read.
