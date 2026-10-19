# Accounting backup, version 2

The backup page exports a ZIP containing `manifest.json` and SHA-256 verified documents. It includes all company-owned rows in 23 accounting tables, including archived rows, invoice lines, cash, checks, stock movements, employees, advances, attendance, salary archives, customer jobs and expenses, costs, payment schedules/history, notification history, manager notes, audit history, and invoice numbering. Credentials, company settings and private chat are excluded. User references contain only source IDs, usernames and email addresses, never passwords or reset codes.

## Restore behavior

- Version 2 restores only into an active company with no existing accounting rows. It never clears or merges populated accounting data. This also prevents duplicate imports.
- All imported primary keys are regenerated. Company IDs are always set from the authenticated session. Cross-record IDs are remapped, including salary archive employee references not declared as EF relationships.
- Existing target users are matched by username AND email within the target company. Required unmatched user references block the whole restore. Optional references to missing/deleted users become null with a warning; stored historical usernames are retained. No accounts are created or modified.
- Documents are first staged outside the public directory, length/hash checked, and moved to a new generated upload directory before the database transaction commits. On failure, database inserts are rolled back and staged/restored files are removed. Existing documents are never overwritten. The upload URLs use the application's current document storage; changing its access controls is a separate step.
- A serializable database transaction protects the export snapshot and restore. It includes all accounting reads/writes. Database and filesystem are not a distributed transaction: a process crash before the database commit may leave an unreferenced generated directory, but no committed record points to files that have not yet been moved.
- Limits: 100 MiB ZIP, 32 MiB manifest, 25 MiB per document, 300 MiB expanded documents, 250,000 records. Export fails explicitly if a referenced document is missing, unsupported, or outside approved uploads. It never labels such a package complete.
- Allowed document extensions match current uploads: PDF, PNG, JPG/JPEG, WEBP, XLS/XLSX and DOCX. Archive entries are never extracted using supplied paths. Generated filenames, a strict table allowlist, exact scalar schemas, unique record/file IDs and relationship validation are enforced.

## Legacy JSON

The existing JSON shape remains accepted and is explicitly described as incomplete. Its available customer cards, stock cards, invoices/lines, checks, and cash movements are restored in one transaction. Existing matching records are preserved. Identical cash entries are compared by multiplicity to avoid losing legitimate duplicates or duplicating them on repeat import. Ambiguous customer references fail the whole import. Legacy JSON has no cash-to-invoice IDs, archived stock movement history, staff, payment schedules, cost details or document bytes; missing data is never invented.

## Validation

Run:

```bash
dotnet run --project Tests/AccountingBackup/AccountingBackup.csproj
dotnet run --project Tests/AccountingConsistency/AccountingConsistency.csproj
```

Backup tests use an isolated SQLite database and temporary documents, never production data. They cover all tables, financial totals, archived records, remapped IDs, duplicate legitimate cash rows, exact document bytes, company isolation, excluded secrets/chat, populated target rejection, unsupported versions, dangling links, path traversal, corruption, missing attachments, injected mid-transaction failure, filesystem failure, missing required users, size limits and legacy compatibility/rollback/idempotency.

No EF model changes or database migration are required. After deploying, download a fresh ZIP from **Araçlar → Yedekleme → Yedek İndir**. Keep it outside the server. Do not test restoration into the live firm's existing records. This feature is manual backup, not automatic daily backup.
