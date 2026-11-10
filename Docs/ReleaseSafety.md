# Independent maintenance and document safety release candidate

Base: 07793b696775ebb30a0d069751f937d5897d49e8. No Admin Portal services, company switching,
new database model or migration is included. This is a local, uncommitted candidate.
The EF model, migration chain and existing accounting audit behavior remain the base version.
The SQLite bundle 2.1.14 reference preserves the already approved security fix; no audit suppression.

## Production startup prerequisites

Keep ASPNETCORE_ENVIRONMENT=Production. The candidate refuses to start in Production if any
of these flags is true (or is not a valid boolean):
- Database__AutoMigrate
- Maintenance__AttachLegacyDataToFirstFirm
- Maintenance__FixDefaultAdminOnStartup

Missing values mean false. Check Render's effective environment and pre-deploy/start commands
separately before release. A Dashboard command could still run migrations outside the app;
this code cannot prevent that. No live schema or Dashboard was inspected during preparation.
Data/AppDbContext.cs, Models and Migrations must remain identical to the base commit.
There is no AdminCompanyTransitions dependency. Missing/newer live schema still needs a
separately approved read-only check; unchanged models do not prove the live database's state.
AdminPortal__EnableCompanySwitch has no effect in this candidate because the feature is absent.

## Maintenance operation (future manual actions, require approval)

1. For a planned deployment set Maintenance__Enabled=true as the startup fallback. Saving
   environment settings can itself trigger a deploy. Back up ephemeral files before any such action.
2. The process reads /app/App_Data/release-maintenance.control before every dynamic admission.
   Only the exact word `open` (optional surrounding whitespace, at most 7 characters read) opens it.
   `closed`, malformed data or IO/permission errors close it. If absent, the startup flag applies.
3. Authorized operators use existing Render SSH/Shell access, not a web-admin bypass. Under the
   application's user, create App_Data if needed, write `closed` to a temporary adjacent file,
   then atomically rename it to release-maintenance.control. Restrict directory/file access to
   that user. Never make a public control endpoint or put credentials in commands.
4. GET /health/live returns only liveness, maintenance, drained and a random process instance ID.
   HEAD is also allowed. Other methods/path suffixes are not health exceptions. Configure the
   Render liveness path separately only with approval. This probe intentionally returns 200
   during maintenance; it does not mean users can access the application.
5. After closing admissions, wait until this process reports maintenance=true and drained=true.
   Requests admitted earlier, including active writes, must finish or fail before drained=true.
   Do not kill timed-out writes or claim they completed: stop and investigate.
6. This is a single-process gate. During the FIRST rollout the old version has no gate. A new
   instance reporting drained=true does NOT prove the old instance stopped. Confirm its full
   termination/drain through an approved external access restriction and deployment procedure
   before calling the whole service quiescent. If that cannot be established, do not mount the disk.
7. Safe static assets can still be served. All dynamic routes, APIs, login and Panel are blocked
   with 503 + Retry-After while closed. This does not clear sessions or expand any user role.
   No role/IP/URL-string-based admin bypass exists. SSH/SFTP remains separate infrastructure access.
8. When disk mount, exact-path file copy, SHA-256 and acceptance checks are complete, write `open`
   via an adjacent temporary file and atomic rename. Recheck maintenance=false. Do not remove the
   control file to reopen when startup fallback=true.
9. App_Data is NOT persistent at the proposed uploads-only mount. With startup fallback=true,
   a restart/deploy losing the control file safely returns to maintenance; an operator must
   deliberately reopen after acceptance. If fallback=false, absence opens normal operation.
   Do not use fallback=false for the protected disk-transition window.

Only request admissions are drained. Existing private-media background expiry cleanup remains
unchanged and may continue; this is not a PostgreSQL-wide read-only mode or a backup consistency
lock for other applications/processes. No other application instance is certified by this health probe.

## Document behavior

- /uploads is never passed to StaticFileMiddleware. Only validated public assets are served.
- /belgeler/ek/{id} and /belgeler/cek/{id} recheck the actual session user, its own firm and
  active firm in the database, then record ownership, path uniqueness and safe path components.
  Private Panel sessions do not acquire accounting document access. Traversal/symlinks fail closed.
- Existing views now use these protected routes. Untrusted old raw uploads links are deliberately
  denied. Existing valid paths in the database and physical document files are not rewritten.
- Upload transactions preserve the base behavior of not adding new attachment audit entries.
  Pre-commit write failure cleans only the new upload. A commit attempt followed by an exception
  preserves the file and does not report success, even if this leaves an orphan for later review.
- Cheque deletion commits the database removal first. Confirmed failure/unknown outcome keeps
  the document. Post-commit file cleanup failure leaves deletion committed and emits a safe warning.
- ZIP staging and final folders are both under uploads. File hashes, package format and ID mapping
  remain unchanged. Cleanup is limited to the operation's directories. An uncertain commit keeps
  final files, returns failure and must not be blindly retried. Process termination may leave staging
  or orphan files; do not delete these without confirming database references.
- On the new disk existing files do not appear automatically. Keep external verified copies,
  transfer under the original relative paths and verify SHA-256. No full ZIP restore into a live firm.

## Validation limits and rollback

This candidate was verified in isolated synthetic SQLite and PostgreSQL 18.6 environments.
The 211 PostgreSQL run completed with 292 PASS, 0 FAIL, 0 BLOCKED: Core 165, Documents 45,
Deletion 57 and CommitNetwork 25. Real TCP connections were closed after PostgreSQL completed
COMMIT but before its acknowledgement reached the application; all four document flows preserved
files and avoided false success. No Render or live database connection was made.

The 227 read-only audit found zero production-source hash changes since the PostgreSQL run,
and all four PostgreSQL packages' application DLLs match the current Release DLL. Test-source
PDB checksums also match. No repeated test run is required for this packaging-only update.
The historical SQLite total is 232 (137 + 45 + 50). Its 137/50 packages predate four Razor link
changes; their other available source checksums match. The 45-check SQLite document/view package
matches the current application. Do not describe all historical SQLite packages as one identical
final binary. Current final views are covered by the 45-check document runs and PostgreSQL suite.
The earlier Admin Portal assembly's 115 PostgreSQL passes are not evidence for this candidate.

Physical Render mount/permissions, the actual live schema and the first old-process shutdown
remain separate operational checks. Synthetic schema creation used EnsureCreated only in new test
DBs; no migrations or live history were applied. A test pass is not permission to deploy or mount.

Rollback only to an identified schema-compatible app version, keeping the persistent disk attached.
Do not drop tables, restore a production DB, detach/delete the disk or discard preserved files to
undo a failed deploy. Keep maintenance active and inspect first. Do not open new customer traffic
until the old process is gone, documents are verified, and the selected release is healthy.
## Türkçe işletim özeti ve yerel sonuç

- Bu aday yalnızca yerel hazırlıktır. Canlı ayar, veritabanı ve Docker kaynakları değiştirilmedi.
- Maintenance__Enabled=true, kontrol dosyası yokken de bakımın kapalı erişimle başlamasını sağlar.
  Bu Render ayarını kaydetmek deploy tetikleyebilir; mevcut dosyalar dışarıda doğrulanmadan yapılmaz.
- Yetkili SSH kullanıcısı /app/App_Data/release-maintenance.control dosyasına komşu geçici dosya ve
  atomik yeniden adlandırma ile `closed` yazar. Geçersiz içerik/erişim hatası da erişimi kapatır.
- GET /health/live çıktısında aynı instance kimliği için maintenance=true ve drained=true görülmeden
  devam edilmez. Eski Render sürecinin de tamamen durduğu ayrıca doğrulanmalıdır.
- Bakımda yeni dinamik GET/POST/API/Panel istekleri 503 alır; başlamış istekler tamamlanır. Sağlık
  kontrolü ve doğrulanmış genel statik dosyalar dışında HTTP istisnası yoktur. SSH ayrı yönetim yoludur.
- Tekrar açmak için ayrıca onayla dosyaya `open` yazılır. Startup fallback true iken dosyayı silmek
  açma işlemi değildir. Yeniden başlatmada geçici kontrol dosyası kaybolursa bakım kapalı erişime döner.
- Belge, disk, hash ve uygulama kabul kontrolleri bitmeden `open` yazılmaz. Firma geçişi bu adayda yoktur.

Doğrulanmış güncel PostgreSQL sonucu: 292 PASS, 0 FAIL, 0 BLOCKED. Dağılım: Core 165,
Documents 45, Deletion 57, CommitNetwork 25. Dört akışta gerçek TCP commit yanıtı kaybı da sınandı.
Tarihsel SQLite sonucu 232 PASS'tır (137 + 45 + 50). 227. adım PDB kontrolünde 137/50 paketlerinin
üretim kaynakları yalnızca dört Razor bağlantı dosyasında farklı çıktı; C# kaynakları eşleşti.
45 SQLite belge/ekran kontrolünün uygulama DLL'i ve bütün PostgreSQL paketleri mevcut DLL ile aynı.
Bu nedenle 232 sayısı tek bir son-binary çalıştırması gibi sunulmamalıdır.

227. adımda PostgreSQL koşumundan beri üretim kaynaklarında hash farkı bulunmadı. 228. adım
sadece Git dışlaması ve rapor düzenlemesidir; kod değişmedi ve testler yeniden çalıştırılmadı.
Bekleyenler: canlı şemanın ayrı onaylı salt okunur kontrolü, ilk yayında eski sürecin kapanış kanıtı
ve Render disk/izin/aktarım doğrulaması. Önceki 115 Admin Portal testi bu adayın sonucu sayılmaz.

Gönderime yalnızca kaynaklar ve dokümanlar alınır. Tests/PostgresReleaseSafety/Artifacts/,
run-state.json ve production-hashes.xml yerelde tutulur ve Git dışıdır. Bu kural dosya silmez
ve önceden izlenen .db/.jpeg dosyalarını izleme dışına çıkarmaz. Ayrı kullanıcı onayı olmadan
commit, push, deploy veya migration yapılmaz.