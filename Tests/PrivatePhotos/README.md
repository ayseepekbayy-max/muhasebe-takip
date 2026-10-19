# Private photo regression checks

Run from the clean repository root:

```powershell
dotnet build Tests/PrivatePhotos/PrivatePhotos.csproj -c Release
dotnet Tests/PrivatePhotos/bin/Release/net10.0/PrivatePhotos.dll
node Tests/PrivatePhotos/photo-viewer.cjs
```

The suite uses the real Panel handlers with an isolated temporary SQLite database and temporary media storage. It verifies view-once, 1/3/5-hour uploads, first-open timing, repeat access without extending deadlines, expiry, unopened 24-hour expiry, concurrent opens, session/sender/hidden-message access, cleanup, text messages, replies, polling and audio access. PostgreSQL model snapshot, additive migration SQL generation and deadline expression translation are checked offline without opening a PostgreSQL connection or applying a migration.

The JavaScript suite parses the page script and exercises actual viewer code with a minimal DOM and controlled timers. It verifies closing and releasing the image at expiry, request elapsed time, hidden pages and manual close. Real mobile gestures, long-press interactions, microphone recording and live PostgreSQL end-to-end behavior require separate smoke tests.

The existing migration ID 20261016090000_AddTimedPrivatePhotos is retained. It has already been applied to production according to the release confirmation; these tests do not apply it. Database__AutoMigrate must remain false. No migration inspection/application tools or release SQL executors are included here.

Private media remains outside wwwroot. Without a persistent disk on Render, deploy/restart can lose photo and audio files before their intended deadline; PostgreSQL backups do not include these files.
