using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
namespace MuhasebeTakip2.App.Services;

public sealed class PrivateMediaOptions
{
    public bool BlockNewUploads { get; set; }
    // Must be chosen from the actual database plan, not guessed from media quota.
    public long? MaxDatabaseBytes { get; set; }
    public long DatabaseReserveBytes { get; set; } = 128L * 1024 * 1024;
}
public sealed class MediaQuotaExceededException() : Exception("Medya kotası dolu; yeni medya gönderilemedi.");
public sealed class MediaStorageUnavailableException(string diagnosticCode = "MEDIA_UNAVAILABLE", string stage = "CAPACITY", string runtime = "UNKNOWN") : Exception("Medya depolaması şu anda kullanılamıyor.") {
    public string DiagnosticCode { get; } = diagnosticCode;
    public string Stage { get; } = stage;
    public string Runtime { get; } = runtime;
}
public sealed class MediaMissingException() : Exception("Medya artık mevcut değil.");

// PostgreSQL only. No filesystem/R2 fallback, URLs, filenames, or independent upload outbox.
public sealed class PrivateMediaStore(AppDbContext db, TimeProvider clock, IOptionsMonitor<PrivateMediaOptions> options)
{
    public const int PhotoLimit = 8 * 1024 * 1024;
    public const int AudioLimit = 16 * 1024 * 1024;
    public const long TotalLimit = 100L * 1024 * 1024;
    public DateTime Now => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    public static bool Expired(PrivateMessage m, DateTime now) => m.Kind != PrivateMessageKind.Text &&
        (m.CreatedAtUtc <= now.AddHours(-24) || m.ExpiredAtUtc != null ||
         (m.Kind == PrivateMessageKind.ViewOncePhoto && m.PhotoAccessExpiresAtUtc <= now));
    // This projection replaces the existing size SELECT. Only catalog metadata is read.
    public sealed class RuntimeCapacity {
        public long DatabaseBytes { get; set; }
        public long RoleOid { get; set; }
        public bool ContentsSelect { get; set; }
        public bool ContentsInsert { get; set; }
        public bool ContentsDelete { get; set; }
        public bool QuotaSelect { get; set; }
        public bool QuotaUpdate { get; set; }
        public bool MessageInsert { get; set; }
        public bool MessageSequence { get; set; }
        public bool QuotaTriggerEnabled { get; set; }
        public bool QuotaFunctionExecute { get; set; }
        public string SafeSummary => $"RoleOid={RoleOid};ContentsSelect={ContentsSelect};ContentsInsert={ContentsInsert};ContentsDelete={ContentsDelete};QuotaSelect={QuotaSelect};QuotaUpdate={QuotaUpdate};MessageInsert={MessageInsert};MessageSequence={MessageSequence};QuotaTrigger={QuotaTriggerEnabled};QuotaFunctionExecute={QuotaFunctionExecute}";
    }
    async Task<RuntimeCapacity> CapacityAsync(CancellationToken token)
    {
        var config = options.CurrentValue;
        if (!db.Database.IsNpgsql()) throw new MediaStorageUnavailableException("MEDIA_PROVIDER");
        if (config.BlockNewUploads) throw new MediaStorageUnavailableException("MEDIA_UPLOADS_BLOCKED");
        if (config.MaxDatabaseBytes is not > 0 || config.DatabaseReserveBytes < 0)
            throw new MediaStorageUnavailableException("MEDIA_CAPACITY_CONFIG");
        // Conservative reserve for the WHOLE logical media allowance. Not a WAL/free-filesystem probe.
        var state = await db.Database.SqlQueryRaw<RuntimeCapacity>("""
            SELECT pg_catalog.pg_database_size(pg_catalog.current_database()) AS "DatabaseBytes",
              (SELECT oid::bigint FROM pg_catalog.pg_roles WHERE rolname=current_user) AS "RoleOid",
              coalesce(pg_catalog.has_table_privilege(pg_catalog.to_regclass('public."PrivateMediaContents"'),'SELECT'),false) AS "ContentsSelect",
              coalesce(pg_catalog.has_table_privilege(pg_catalog.to_regclass('public."PrivateMediaContents"'),'INSERT'),false) AS "ContentsInsert",
              coalesce(pg_catalog.has_table_privilege(pg_catalog.to_regclass('public."PrivateMediaContents"'),'DELETE'),false) AS "ContentsDelete",
              coalesce(pg_catalog.has_table_privilege(pg_catalog.to_regclass('public."PrivateMediaQuotas"'),'SELECT'),false) AS "QuotaSelect",
              coalesce(pg_catalog.has_column_privilege(pg_catalog.to_regclass('public."PrivateMediaQuotas"'),'UsedBytes','UPDATE'),false) AS "QuotaUpdate",
              coalesce(pg_catalog.has_table_privilege(pg_catalog.to_regclass('public."PrivateMessages"'),'INSERT'),false) AS "MessageInsert",
              coalesce(pg_catalog.has_sequence_privilege(pg_catalog.pg_get_serial_sequence('public."PrivateMessages"','Id'),'USAGE'),false) AS "MessageSequence",
              EXISTS(SELECT 1 FROM pg_catalog.pg_trigger WHERE tgrelid=pg_catalog.to_regclass('public."PrivateMediaContents"') AND tgname='firmova_private_media_quota_trigger' AND tgenabled IN ('O','A')) AS "QuotaTriggerEnabled",
              coalesce(pg_catalog.has_function_privilege(pg_catalog.to_regprocedure('public.firmova_private_media_quota()'),'EXECUTE'),false) AS "QuotaFunctionExecute"
            """).SingleAsync(token);
        var size=state.DatabaseBytes;
        if (size > config.MaxDatabaseBytes.Value || config.MaxDatabaseBytes.Value - size < 4 * TotalLimit ||
            config.MaxDatabaseBytes.Value - size - 4 * TotalLimit < config.DatabaseReserveBytes)
            throw new MediaStorageUnavailableException("MEDIA_CAPACITY_HEADROOM",runtime:state.SafeSummary);
        return state;
    }
    public async Task SaveAsync(PrivateMessage message, byte[] bytes, CancellationToken token)
    {
        var photo = message.Kind == PrivateMessageKind.ViewOncePhoto;
        if (message.Kind is not (PrivateMessageKind.ViewOncePhoto or PrivateMessageKind.Audio) ||
            bytes.Length == 0 || bytes.Length > (photo ? PhotoLimit : AudioLimit) ||
            (photo && message.PhotoViewHours is not (null or 1 or 3 or 5)) || Detect(bytes, photo) is not string type)
            throw new ArgumentException("Geçersiz medya.");
        message.MediaContentType = type; message.MediaKey = Guid.NewGuid().ToString("N"); message.CreatedAtUtc = Now;
        var stage="BEGIN";var runtime="UNKNOWN";
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
            stage="CAPACITY";runtime=(await CapacityAsync(token)).SafeSummary;
            stage="MESSAGE_INSERT";db.PrivateMessages.Add(message); await db.SaveChangesAsync(token);
            db.PrivateMediaContents.Add(new() { MessageId = message.Id, MediaKey = message.MediaKey,
                Payload = bytes.ToArray(), CreatedAtUtc = message.CreatedAtUtc, ExpiresAtUtc = message.CreatedAtUtc.AddHours(24) });
            // PostgreSQL trigger locks the quota row, including raw inserts and FK cascades.
            stage="PAYLOAD_INSERT";await db.SaveChangesAsync(token);
            stage="COMMIT";await tx.CommitAsync(token);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "P0001", MessageText: "PRIVATE_MEDIA_QUOTA" })
            { throw new MediaQuotaExceededException(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.NpgsqlException && ex.InnerException is not Npgsql.PostgresException { SqlState: "23503" })
            { throw new MediaStorageUnavailableException(DatabaseFailureCode(ex.InnerException),stage,runtime); }
        catch (OperationCanceledException) { throw; }
        catch (Npgsql.NpgsqlException ex) { throw new MediaStorageUnavailableException(DatabaseFailureCode(ex),stage,runtime); }
    }
    // Fixed codes only; never attach exception objects, SQL, parameters or connection details.
    static string DatabaseFailureCode(Exception? error) => error switch {
        Npgsql.PostgresException { SqlState: "42501" } => "MEDIA_DB_PERMISSION",
        Npgsql.PostgresException { SqlState: "42P01" or "42703" } => "MEDIA_DB_SCHEMA",
        Npgsql.PostgresException { SqlState: "23514" } => "MEDIA_DB_CONSTRAINT",
        Npgsql.PostgresException { SqlState: "25006" } => "MEDIA_DB_READ_ONLY",
        Npgsql.PostgresException { SqlState: "P0001" } => "MEDIA_DB_TRIGGER",
        Npgsql.PostgresException => "MEDIA_DB_SERVER",
        _ => "MEDIA_DB_CONNECTION"
    };
    public async Task<byte[]> ReadAsync(PrivateMessage message, CancellationToken token)
    {
        if (message.MediaKey == null || Expired(message, Now)) throw new MediaMissingException();
        var now = Now;
        return await db.PrivateMediaContents.AsNoTracking().Where(x => x.MessageId == message.Id && x.MediaKey == message.MediaKey && x.ExpiresAtUtc > now)
            .Select(x => x.Payload).SingleOrDefaultAsync(token) ?? throw new MediaMissingException();
    }
    public Task<int> DeleteAsync(string? key, CancellationToken token) =>
        db.PrivateMediaContents.Where(x => x.MediaKey == key).ExecuteDeleteAsync(token);
    public async Task CleanupAsync(CancellationToken token)
    {
        var now = Now; var cutoff = now.AddHours(-24);
        var ids = await db.PrivateMessages.AsNoTracking().Where(m => m.Kind != PrivateMessageKind.Text &&
            (m.CreatedAtUtc <= cutoff || m.ExpiredAtUtc != null || m.PhotoAccessExpiresAtUtc <= now) &&
            (m.ExpiredAtUtc == null || m.MediaKey != null || db.PrivateMediaContents.Any(x => x.MessageId == m.Id)))
            .OrderBy(m => m.Id).Select(m => m.Id).Take(100).ToListAsync(token);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
            var changed = await db.PrivateMessages.Where(m => m.Id == id && m.Kind != PrivateMessageKind.Text &&
                (m.CreatedAtUtc <= cutoff || m.ExpiredAtUtc != null || m.PhotoAccessExpiresAtUtc <= now))
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ExpiredAtUtc, m => m.ExpiredAtUtc ?? now), token);
            if (changed != 0)
            {
                await db.PrivateMediaContents.Where(x => x.MessageId == id).ExecuteDeleteAsync(token);
                await db.PrivateMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.MediaKey, (string?)null).SetProperty(m => m.MediaContentType, (string?)null), token);
            }
            await tx.CommitAsync(token);
        }
    }
    public static string? Detect(byte[] bytes, bool photo)
    {
        var b = bytes.AsSpan();
        if (photo)
        {
            if (b.Length >= 33 && b[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
                b.Slice(12,4).SequenceEqual("IHDR"u8) && b[^8..^4].SequenceEqual("IEND"u8)) return "image/png";
            if (b.Length >= 32 && b[0] == 255 && b[1] == 216 && b[2] == 255 && b[^2] == 255 && b[^1] == 217) return "image/jpeg";
        }
        else
        {
            if (b.Length > 32 && b[..4].SequenceEqual(new byte[] {26,69,223,163}) &&
                b[..Math.Min(b.Length,4096)].IndexOf("webm"u8) >= 0 && b.IndexOf("OpusHead"u8) >= 0) return "audio/webm";
            if (b.Length > 32 && b[..4].SequenceEqual("OggS"u8) && b[..Math.Min(b.Length,4096)].IndexOf("OpusHead"u8) >= 0) return "audio/ogg";
            if (b.Length > 32 && b.Slice(4,4).SequenceEqual("ftyp"u8) &&
                (b.Slice(8,4).SequenceEqual("M4A "u8) || b.Slice(8,4).SequenceEqual("mp42"u8) || b.Slice(8,4).SequenceEqual("isom"u8) || b.Slice(8,4).SequenceEqual("iso5"u8) || b.Slice(8,4).SequenceEqual("iso6"u8)) &&
                b.IndexOf("soun"u8) >= 0 && b.IndexOf("vide"u8) < 0) return "audio/mp4";
        }
        return null;
    }
}
