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
public sealed class MediaStorageUnavailableException() : Exception("Medya depolaması şu anda kullanılamıyor.");
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
    async Task CapacityAsync(CancellationToken token)
    {
        var config = options.CurrentValue;
        if (!db.Database.IsNpgsql() || config.BlockNewUploads || config.MaxDatabaseBytes is not > 0 || config.DatabaseReserveBytes < 0)
            throw new MediaStorageUnavailableException();
        // Conservative reserve for the WHOLE logical media allowance. Not a WAL/free-filesystem probe.
        var size = await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(token);
        if (size > config.MaxDatabaseBytes.Value || config.MaxDatabaseBytes.Value - size < 4 * TotalLimit ||
            config.MaxDatabaseBytes.Value - size - 4 * TotalLimit < config.DatabaseReserveBytes)
            throw new MediaStorageUnavailableException();
    }
    public async Task SaveAsync(PrivateMessage message, byte[] bytes, CancellationToken token)
    {
        var photo = message.Kind == PrivateMessageKind.ViewOncePhoto;
        if (message.Kind is not (PrivateMessageKind.ViewOncePhoto or PrivateMessageKind.Audio) ||
            bytes.Length == 0 || bytes.Length > (photo ? PhotoLimit : AudioLimit) ||
            (photo && message.PhotoViewHours is not (null or 1 or 3 or 5)) || Detect(bytes, photo) is not string type)
            throw new ArgumentException("Geçersiz medya.");
        message.MediaContentType = type; message.MediaKey = Guid.NewGuid().ToString("N"); message.CreatedAtUtc = Now;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
            await CapacityAsync(token);
            db.PrivateMessages.Add(message); await db.SaveChangesAsync(token);
            db.PrivateMediaContents.Add(new() { MessageId = message.Id, MediaKey = message.MediaKey,
                Payload = bytes.ToArray(), CreatedAtUtc = message.CreatedAtUtc, ExpiresAtUtc = message.CreatedAtUtc.AddHours(24) });
            // PostgreSQL trigger locks the quota row, including raw inserts and FK cascades.
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "P0001", MessageText: "PRIVATE_MEDIA_QUOTA" })
            { throw new MediaQuotaExceededException(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.NpgsqlException && ex.InnerException is not Npgsql.PostgresException { SqlState: "23503" })
            { throw new MediaStorageUnavailableException(); }
        catch (OperationCanceledException) { throw; }
        catch (Npgsql.NpgsqlException) { throw new MediaStorageUnavailableException(); }
    }
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
