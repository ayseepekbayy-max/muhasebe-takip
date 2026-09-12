using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PanelModel(PrivateAccessService privateAccess, AppDbContext db) : PageModel
{
    public string OtherPersonName { get; private set; } = "";
    public string CurrentPersonName { get; private set; } = "";
    public int CurrentPerson { get; private set; }
    public List<PrivateMessage> Messages { get; private set; } = new();

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
        await LoadPresenceAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetMessagesAsync()
    {
        if (!HasAccess)
            return Unauthorized();

        var messages = await LoadMessagesAsync();
        await LoadPresenceAsync();
        return new JsonResult(new { serverNowUtc = ServerNowUtc, presence = new { showLastSeen = ShowLastSeen, otherLastSeenAtUtc = OtherLastSeenAtUtc, otherTyping = OtherTypingRemainingMs > 0, otherTypingRemainingMs = OtherTypingRemainingMs }, messages = messages.Select(message => new
        {
            id = message.Id,
            senderPerson = message.SenderPerson,
            senderName = message.SenderName,
            content = message.Content,
            createdAtUtc = message.CreatedAtUtc
        }) });
    }

    public async Task<IActionResult> OnPostSendAsync([FromForm] string? content)
    {
        if (!HasAccess)
            return Unauthorized();

        var senderName = HttpContext.Session.GetString("PrivatePersonName");
        if (string.IsNullOrWhiteSpace(senderName) || senderName.Length > PrivateMessage.MaxSenderNameLength)
            return Unauthorized();

        content = content?.Trim();
        if (string.IsNullOrEmpty(content) || content.Length > PrivateMessage.MaxContentLength)
            return BadRequest(new { error = "Mesaj 1 ile 4000 karakter arasında olmalıdır." });

        db.PrivateMessages.Add(new PrivateMessage
        {
            SenderPerson = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2,
            SenderName = senderName,
            Content = content,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromForm] int id)
    {
        if (!HasAccess)
            return Unauthorized();

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
        return deleted == 0 ? NotFound() : new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteSelectedAsync([FromForm] int[]? ids)
    {
        if (!HasAccess)
            return Unauthorized();
        if (ids is null || ids.Length is 0 or > 200 || ids.Any(id => id <= 0))
            return BadRequest();

        var person = HttpContext.Session.GetString("PrivatePerson") == "1" ? 1 : 2;
        var selectedIds = ids.Distinct().ToArray();
        var messages = await db.PrivateMessages.AsNoTracking()
            .Where(message => selectedIds.Contains(message.Id))
            .Select(message => new { message.Id, message.SenderPerson })
            .ToListAsync(HttpContext.RequestAborted);
        // Reject the entire batch before deleting anything when any ID belongs to the other person.
        if (messages.Any(message => message.SenderPerson != person))
            return StatusCode(StatusCodes.Status403Forbidden);
        if (messages.Count != selectedIds.Length)
            return NotFound();

        await db.PrivateMessages.Where(message => selectedIds.Contains(message.Id) && message.SenderPerson == person)
            .ExecuteDeleteAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteAllAsync([FromForm] bool confirmed)
    {
        if (!HasAccess)
            return Unauthorized();
        if (!confirmed)
            return BadRequest();

        await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        await db.PrivateMessages.ExecuteDeleteAsync(HttpContext.RequestAborted);
        await db.PrivatePresences.ExecuteUpdateAsync(update => update.SetProperty(presence => presence.MessagesClearedAtUtc, (DateTime?)null), HttpContext.RequestAborted);
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