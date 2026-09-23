using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

[RequestSizeLimit(17 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 17 * 1024 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PanelModel(PrivateAccessService privateAccess, AppDbContext db, PrivateMediaStore media) : PageModel
{
    public string OtherPersonName { get; private set; } = "";
    public string CurrentPersonName { get; private set; } = "";
    public int CurrentPerson { get; private set; }
    public List<PrivateMessage> Messages { get; private set; } = new();
    public Dictionary<int, ReplyPreview> ReplyPreviews { get; private set; } = new();
    public sealed record ReplyPreview(int? Id, string? SenderName, string Text);

    public static string PreviewText(PrivateMessage message) => message.Kind switch
    {
        PrivateMessageKind.Audio => "🎤 Sesli mesaj",
        PrivateMessageKind.ViewOncePhoto => "📷 Fotoğraf",
        _ => message.Content.Length > 160 ? message.Content[..160] + "…" : message.Content
    };

    public bool ShowLastSeen { get; private set; } = true;
    public DateTime? OtherLastSeenAtUtc { get; private set; }

    public int OtherTypingRemainingMs { get; private set; }
    public DateTime ServerNowUtc => DateTime.UtcNow;

    private bool HasAccess => HttpContext.Session.GetString("PrivateMode") == "1" &&
        HttpContext.Session.GetString("PrivatePerson") is "1" or "2";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!HasAccess)
            return RedirectToPage("/Login");

        CurrentPerson = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        OtherPersonName = privateAccess.GetOtherPersonName(HttpContext.Session.GetString("PrivatePerson")!);
        CurrentPersonName = HttpContext.Session.GetString("PrivatePersonName") ?? "";
        Messages = await LoadMessagesAsync();
        await LoadReplyPreviewsAsync(Messages);
        await LoadPresenceAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetMessagesAsync()
    {
        if (!HasAccess)
            return Unauthorized();

        var messages = await LoadMessagesAsync();
        await LoadReplyPreviewsAsync(messages);
        await LoadPresenceAsync();
        return new JsonResult(new { serverNowUtc = ServerNowUtc, presence = new { showLastSeen = ShowLastSeen, otherLastSeenAtUtc = OtherLastSeenAtUtc, otherTyping = OtherTypingRemainingMs > 0, otherTypingRemainingMs = OtherTypingRemainingMs }, messages = messages.Select(message => new
        {
            id = message.Id,
            senderPerson = message.SenderPerson,
            senderName = message.SenderName,
            content = message.Content,
            reply = ReplyPreviews.GetValueOrDefault(message.Id),
            kind = (int)message.Kind,
            viewed = message.ViewedAtUtc.HasValue,
            expired = PhotoExpired(message),
            available = message.MediaKey != null,
            durationSeconds = message.DurationSeconds,
            createdAtUtc = message.CreatedAtUtc
        }) });
    }

    public async Task<IActionResult> OnPostSendAsync([FromForm] string? content, [FromForm] int? replyToMessageId)
    {
        if (!HasAccess)
            return Unauthorized();

        var senderName = HttpContext.Session.GetString("PrivatePersonName");
        if (string.IsNullOrWhiteSpace(senderName) || senderName.Length > PrivateMessage.MaxSenderNameLength)
            return Unauthorized();

        if (!ModelState.IsValid || !await CanReplyAsync(replyToMessageId)) return InvalidReply();

        content = content?.Trim();
        if (string.IsNullOrEmpty(content) || content.Length > PrivateMessage.MaxContentLength)
            return BadRequest(new { error = "Mesaj 1 ile 4000 karakter arasında olmalıdır." });

        db.PrivateMessages.Add(new PrivateMessage
        {
            SenderPerson = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2,
            SenderName = senderName,
            Content = content,
            ReplyToMessageId = replyToMessageId, IsReply = replyToMessageId.HasValue,
            CreatedAtUtc = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(HttpContext.RequestAborted); }
        catch (DbUpdateException ex) when (replyToMessageId.HasValue && IsMissingReply(ex)) { return InvalidReply(); }
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostMediaAsync(IFormFile? file, [FromForm] string? kind, [FromForm] int? durationSeconds, [FromForm] int? replyToMessageId)
    {
        if (!HasAccess) return Unauthorized();
        var name = HttpContext.Session.GetString("PrivatePersonName");
        if (string.IsNullOrWhiteSpace(name) || name.Length > PrivateMessage.MaxSenderNameLength) return Unauthorized();
        if (!ModelState.IsValid || !await CanReplyAsync(replyToMessageId)) return InvalidReply();
        if (kind is not ("photo" or "audio")) return BadRequest();
        var photo = kind == "photo";
        if (!photo && (durationSeconds is null or < 1 or > 305)) return BadRequest(new { error = "Ses kaydı süresi geçersiz (en fazla 5 dakika)." });
        var limit = photo ? PrivateMediaStore.PhotoLimit : PrivateMediaStore.AudioLimit;
        if (file is null || file.Length == 0 || file.Length > limit)
            return BadRequest(new { error = photo ? "Fotoğraf en fazla 8 MB olabilir." : "Ses kaydı en fazla 16 MB olabilir." });
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, HttpContext.RequestAborted);
        var bytes = buffer.ToArray();
        var type = PrivateMediaStore.Detect(bytes, photo);
        if (type is null) return BadRequest(new { error = "Desteklenmeyen dosya. Fotoğraf: JPEG/PNG; ses: WebM/Opus, Ogg/Opus veya M4A/AAC." });
        var key = await media.SaveAsync(bytes, HttpContext.RequestAborted);
        try
        {
            db.PrivateMessages.Add(new PrivateMessage {
                SenderPerson = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2,
                SenderName = name, Content = "", CreatedAtUtc = DateTime.UtcNow,
                ReplyToMessageId = replyToMessageId, IsReply = replyToMessageId.HasValue,
                Kind = photo ? PrivateMessageKind.ViewOncePhoto : PrivateMessageKind.Audio,
                MediaKey = key, MediaContentType = type, DurationSeconds = photo ? null : durationSeconds
            });
            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (DbUpdateException ex) when (replyToMessageId.HasValue && IsMissingReply(ex)) { media.Delete(key); return InvalidReply(); }
        catch { media.Delete(key); throw; }
        return new JsonResult(new { success = true });
    }

    private IQueryable<PrivateMessage> VisibleMessages()
    {
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        return db.PrivateMessages.Where(m =>
            !db.PrivateMessageHiddens.Any(h => h.PersonNumber == person && h.PrivateMessageId == m.Id) &&
            !db.PrivatePresences.Any(p => p.PersonNumber == person && p.MessagesClearedAtUtc.HasValue && m.CreatedAtUtc <= p.MessagesClearedAtUtc.Value));
    }

    private IQueryable<PrivateMessage> AccessibleMedia(int id) => VisibleMessages().Where(m => m.Id == id);

    private async Task<bool> CanReplyAsync(int? id) => id is null ||
        (id > 0 && await VisibleMessages().AnyAsync(m => m.Id == id, HttpContext.RequestAborted));

    private BadRequestObjectResult InvalidReply() => BadRequest(new { error = "Yanıtlanacak mesaj kullanılamıyor." });

    // A concurrent delete can win between validation and insertion. Do not expose DB details.
    private static bool IsMissingReply(DbUpdateException error) => error.InnerException is
        Npgsql.PostgresException { SqlState: "23503" } or
        Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 787 };

    private async Task LoadReplyPreviewsAsync(List<PrivateMessage> messages)
    {
        var ids = messages.Where(m => m.IsReply && m.ReplyToMessageId.HasValue)
            .Select(m => m.ReplyToMessageId!.Value).Distinct().ToArray();
        // Fetch only visible text/identity, never media keys or duplicated attachments.
        var originals = await VisibleMessages().AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => new PrivateMessage { Id = m.Id, SenderName = m.SenderName, Kind = m.Kind,
                Content = m.Kind == PrivateMessageKind.Text ? m.Content.Substring(0, Math.Min(m.Content.Length, 161)) : "" })
            .ToDictionaryAsync(m => m.Id, HttpContext.RequestAborted);
        ReplyPreviews = messages.Where(m => m.IsReply).ToDictionary(m => m.Id, m =>
            m.ReplyToMessageId is not int id ? new ReplyPreview(null, null, "Silinmiş mesaj") :
            originals.TryGetValue(id, out var original) ? new ReplyPreview(id, original.SenderName, PreviewText(original)) :
            new ReplyPreview(null, null, "Mesaj kullanılamıyor"));
    }

    public async Task<IActionResult> OnGetAudioAsync(int id)
    {
        if (!HasAccess) return Unauthorized();
        var message = await AccessibleMedia(id).AsNoTracking().SingleOrDefaultAsync(m => m.Kind == PrivateMessageKind.Audio);
        if (message?.MediaKey is null) return NotFound(new { error = "Ses kaydı bulunamadı." });
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return new FileContentResult(await media.ReadAsync(message.MediaKey, HttpContext.RequestAborted), message.MediaContentType!) { EnableRangeProcessing = true }; }
        catch (FileNotFoundException) { return NotFound(new { error = "Ses kaydı bulunamadı." }); }
        catch (DirectoryNotFoundException) { return NotFound(new { error = "Ses kaydı bulunamadı." }); }
    }

    public static bool PhotoExpired(PrivateMessage message) =>
        message.Kind == PrivateMessageKind.ViewOncePhoto && !message.ViewedAtUtc.HasValue &&
        (message.ExpiredAtUtc.HasValue || message.CreatedAtUtc <= DateTime.UtcNow.AddHours(-24));

    public async Task<IActionResult> OnPostPhotoAsync([FromForm] int id)
    {
        if (!HasAccess) return Unauthorized();
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        // Also prevent caching expired and missing media responses.
        Response.OnStarting(() => {
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var query = AccessibleMedia(id).Where(m => m.Kind == PrivateMessageKind.ViewOncePhoto &&
            m.SenderPerson != person && m.ViewedAtUtc == null && m.ExpiredAtUtc == null &&
            m.CreatedAtUtc > cutoff && m.MediaKey != null);
        // Conditional database update serializes competing requests across devices/instances.
        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        if (await query.ExecuteUpdateAsync(u => u.SetProperty(m => m.ViewedAtUtc, DateTime.UtcNow), HttpContext.RequestAborted) != 1)
            return StatusCode(410, new { error = "Fotoğraf görüntülendi, süresi doldu veya artık erişilemiyor." });
        var message = await db.PrivateMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        byte[] bytes;
        try
        {
            bytes = await media.ReadAsync(message.MediaKey!, HttpContext.RequestAborted);
            // Never deliver the image unless its source file has been deleted.
            media.Delete(message.MediaKey);
        }
        catch (FileNotFoundException) { bytes = []; }
        catch (DirectoryNotFoundException) { bytes = []; }
        await db.PrivateMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.MediaKey, (string?)null).SetProperty(m => m.MediaContentType, (string?)null)
            .SetProperty(m => m.ViewedAtUtc, bytes.Length == 0 ? (DateTime?)null : message.ViewedAtUtc));
        await transaction.CommitAsync(CancellationToken.None);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return bytes.Length == 0 ? StatusCode(410, new { error = "Fotoğraf artık mevcut değil." }) : File(bytes, message.MediaContentType!);
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromForm] int id)
    {
        if (!HasAccess)
            return Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var message = await db.PrivateMessages.AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == id, HttpContext.RequestAborted);
        if (message is null)
            return NotFound();
        if (message.SenderPerson != person)
            return StatusCode(StatusCodes.Status403Forbidden);

        var deleted = await db.PrivateMessages
            .Where(message => message.Id == id && message.SenderPerson == person)
            .ExecuteDeleteAsync(HttpContext.RequestAborted);
        if (deleted != 0) media.Delete(message.MediaKey);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return deleted == 0 ? NotFound() : new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteSelectedAsync([FromForm] int[]? ids)
    {
        if (!HasAccess)
            return Unauthorized();
        if (ids is null || ids.Length is 0 or > 200 || ids.Any(id => id <= 0))
            return BadRequest();

        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var selectedIds = ids.Distinct().ToArray();
        var messages = await db.PrivateMessages.AsNoTracking()
            .Where(message => selectedIds.Contains(message.Id))
            .Select(message => new { message.Id, message.SenderPerson, message.MediaKey })
            .ToListAsync(HttpContext.RequestAborted);
        // Reject the entire batch before deleting anything when any ID belongs to the other person.
        if (messages.Any(message => message.SenderPerson != person))
            return StatusCode(StatusCodes.Status403Forbidden);
        if (messages.Count != selectedIds.Length)
            return NotFound();

        await db.PrivateMessages.Where(message => selectedIds.Contains(message.Id) && message.SenderPerson == person)
            .ExecuteDeleteAsync(HttpContext.RequestAborted);
        foreach (var message in messages) media.Delete(message.MediaKey);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteAllAsync([FromForm] bool confirmed)
    {
        if (!HasAccess)
            return Unauthorized();
        if (!confirmed)
            return BadRequest();

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, HttpContext.RequestAborted);
        var keys = await db.PrivateMessages.Where(m => m.MediaKey != null).Select(m => m.MediaKey).ToListAsync(HttpContext.RequestAborted);
        await db.PrivateMessages.ExecuteDeleteAsync(HttpContext.RequestAborted);
        await db.PrivatePresences.ExecuteUpdateAsync(update => update.SetProperty(presence => presence.MessagesClearedAtUtc, (DateTime?)null), HttpContext.RequestAborted);
        foreach (var key in keys) media.Delete(key);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public Task<IActionResult> OnPostHideAsync([FromForm] int id) => HideMessagesAsync(new[] { id });

    public Task<IActionResult> OnPostHideSelectedAsync([FromForm] int[]? ids) => HideMessagesAsync(ids);

    private async Task<IActionResult> HideMessagesAsync(int[]? ids)
    {
        if (!HasAccess)
            return Unauthorized();
        if (ids is null || ids.Length is 0 or > 200 || ids.Any(id => id <= 0))
            return BadRequest();

        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var selectedIds = ids.Distinct().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var found = await db.PrivateMessages.CountAsync(message => selectedIds.Contains(message.Id), HttpContext.RequestAborted);
        if (found != selectedIds.Length)
            return NotFound();

        var now = DateTime.UtcNow;
        foreach (var id in selectedIds)
        {
            // Server-derived person, parameterized values, and a unique key make repeats idempotent.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PrivateMessageHidden" ("PersonNumber", "PrivateMessageId", "HiddenAtUtc")
                SELECT {person}, "Id", {now} FROM "PrivateMessages" WHERE "Id" = {id}
                ON CONFLICT ("PersonNumber", "PrivateMessageId") DO NOTHING
                """, HttpContext.RequestAborted);
        }
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostClearMineAsync()
    {
        if (!HasAccess)
            return Unauthorized();

        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PrivatePresence" ("PersonNumber", "LastSeenAtUtc", "ShowLastSeen", "MessagesClearedAtUtc")
            VALUES ({person}, {now}, {true}, {now})
            ON CONFLICT ("PersonNumber") DO UPDATE SET "MessagesClearedAtUtc" = EXCLUDED."MessagesClearedAtUtc"
            """, HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostSettingsAsync([FromForm] bool? showLastSeen)
    {
        if (!HasAccess)
            return Unauthorized();
        if (showLastSeen is null)
            return BadRequest();

        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var now = DateTime.UtcNow;
        // Atomic upsert also handles a first settings request from multiple tabs.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PrivatePresence" ("PersonNumber", "LastSeenAtUtc", "ShowLastSeen")
            VALUES ({person}, {now}, {showLastSeen.Value})
            ON CONFLICT ("PersonNumber") DO UPDATE SET "ShowLastSeen" = EXCLUDED."ShowLastSeen"
            """, HttpContext.RequestAborted);
        return new JsonResult(new { showLastSeen = showLastSeen.Value });
    }

    public async Task<IActionResult> OnPostTypingAsync([FromForm] bool? typing)
    {
        if (!HasAccess)
            return Unauthorized();
        if (typing is null)
            return BadRequest();

        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var now = DateTime.UtcNow;
        DateTime? updatedAt = typing.Value ? now : null;
        // Null means stopped; a timestamp expires after five seconds without a heartbeat.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PrivatePresence" ("PersonNumber", "LastSeenAtUtc", "ShowLastSeen", "TypingUpdatedAtUtc")
            VALUES ({person}, {now}, {true}, {updatedAt})
            ON CONFLICT ("PersonNumber") DO UPDATE SET "TypingUpdatedAtUtc" = EXCLUDED."TypingUpdatedAtUtc"
            """, HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }
    private async Task LoadPresenceAsync()
    {
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-30);
        var presences = await db.PrivatePresences.AsNoTracking().ToListAsync(HttpContext.RequestAborted);
        var own = presences.SingleOrDefault(presence => presence.PersonNumber == person);
        if (own is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PrivatePresence" ("PersonNumber", "LastSeenAtUtc", "ShowLastSeen")
                VALUES ({person}, {now}, {true}) ON CONFLICT ("PersonNumber") DO NOTHING
                """, HttpContext.RequestAborted);
            own = await db.PrivatePresences.AsNoTracking().SingleAsync(presence => presence.PersonNumber == person, HttpContext.RequestAborted);
        }
        else if (own.LastSeenAtUtc <= cutoff)
        {
            // The predicate prevents simultaneous tabs/requests from repeatedly writing the timestamp.
            await db.PrivatePresences.Where(presence => presence.PersonNumber == person && presence.LastSeenAtUtc <= cutoff)
                .ExecuteUpdateAsync(update => update.SetProperty(presence => presence.LastSeenAtUtc, now), HttpContext.RequestAborted);
        }
        ShowLastSeen = own.ShowLastSeen;
        var other = presences.SingleOrDefault(presence => presence.PersonNumber != person);
        // Typing is independent of last-seen privacy; expose only its short remaining lifetime.
        OtherTypingRemainingMs = other?.TypingUpdatedAtUtc is DateTime typedAt
            ? (int)Math.Clamp((typedAt.AddSeconds(5) - now).TotalMilliseconds, 0, 5000) : 0;
        // A hidden timestamp must never be included in HTML or JSON.
        OtherLastSeenAtUtc = other is { ShowLastSeen: true }
            ? DateTime.SpecifyKind(other.LastSeenAtUtc, DateTimeKind.Utc) : null;
    }

    private async Task<List<PrivateMessage>> LoadMessagesAsync()
    {
        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var clearedAt = await db.PrivatePresences.Where(presence => presence.PersonNumber == person)
            .Select(presence => presence.MessagesClearedAtUtc).SingleOrDefaultAsync(HttpContext.RequestAborted);
        var recent = await db.PrivateMessages.AsNoTracking()
            .Where(message => (!clearedAt.HasValue || message.CreatedAtUtc > clearedAt.Value) &&
                !db.PrivateMessageHiddens.Any(hidden => hidden.PersonNumber == person && hidden.PrivateMessageId == message.Id))
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .Take(200)
            .ToListAsync(HttpContext.RequestAborted);
        return recent.OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.Id).ToList();
    }

    public IActionResult OnPostLogout()
    {
        if (!HasAccess)
            return RedirectToPage("/Login");

        HttpContext.Session.Clear();
        return RedirectToPage("/Login");
    }
}
