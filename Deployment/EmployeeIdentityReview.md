# Employee and historical salary identity - local review

No production connection, data repair, push or deployment performed. No 2-to-6 mapping is present.

## Scope

- Preserve employee IDs on archive/reactivation. Archiving does not delete payroll or attendance.
- A new person always receives a fresh database ID. Names are never identity keys.
- New salary/advance/attendance writes lock the exact active company/employee row in their transaction; lock timeout is five seconds.
- Composite company/employee foreign keys restrict physical deletion and cross-company references.
- Salary `CalisanId` is the original historical reference. `DogrulanmisCalisanId` is the nullable verified reference. No matching/repair endpoint is introduced.
- Only verified salary links are used for employee history. Unknown salaries stay unresolved, even if the original integer later matches a new employee.

## Migration

`20261119160000_PreserveEmployeeSalaryIdentity` is an additive, reviewed PostgreSQL migration. It changes employee/advance company nullability, adds the employee composite key, replaces advance/attendance employee FKs with restricted composite FKs, and adds the verified salary field/index/FK.

Backfill copies only an already-valid positive employee ID in the same company into the new field. It never searches names, never changes original IDs/amounts/dates and never maps a missing employee to a different ID. Null ownership or invalid existing movement/attendance relationships aborts the transaction; no guessed repair.

EF snapshot and target metadata are generated from the complete application model. Existing document objects are reflected as existing prerequisites, not created by this migration. Missing older migration history is not reconstructed or edited. Tests execute only this migration's real generated operations against an isolated pre-change employee model, not a fabricated full migration chain.

`Down` deliberately stops: verified links must not be silently dropped. Do not automatically downgrade/drop the field after new restores or reviewed mappings. On failure during installation the transaction rolls back. Any later rollback needs a separately reviewed data-preserving procedure and a verified backup.

## Backup compatibility

Accounting package version 5 stores the original salary reference and verified link separately. Verified links remap through source employee IDs. Original salary IDs remain unchanged. Versions 2/3/4 normalize in memory without touching the source; unresolved declarations remain explicit and never become guessed employees. JSON compatibility imports do not contain employee data and keep their existing separate consent.

The AES-GCM envelope, key source, key versions, admission/memory limits, document protection, tenant authorization and empty-target restore rule remain unchanged. Old application versions cannot read v5 packages; do not use them as rollback targets after adopting the new format.

## Required production steps (not executed)

1. Review current production schema/history, read-only readiness counts and exact constraint names. Null company ownership or invalid advance/attendance parents are blockers; do not modify them automatically. Existing unresolved salary rows are preserved, not blockers to this migration.
2. Verify a restorable pre-change backup and agree a forward recovery plan. Keep the current version and keys recoverable.
3. Obtain separate approval for the exact PostgreSQL migration, then deploy the matching application. Do not use startup auto-migration or replay unknown migrations.
4. Read-only verify schema and untouched original salary values; manually review any future identity correction under separate approval.

SQLite migration deployment is outside this PostgreSQL-only change; no SQLite migration history has been invented.
