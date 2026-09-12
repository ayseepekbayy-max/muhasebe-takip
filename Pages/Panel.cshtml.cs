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
        return Page();
    }

    public async Task<IActionResult> OnGetMessagesAsync()
    {
        if (!HasAccess)
            return Unauthorized();

        var messages = await LoadMessagesAsync();
        return new JsonResult(messages.Select(message => new
        {
            id = message.Id,
            senderPerson = message.SenderPerson,
            senderName = message.SenderName,
            content = message.Content,
            createdAtUtc = message.CreatedAtUtc
        }));
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

    private async Task<List<PrivateMessage>> LoadMessagesAsync()
    {
        var recent = await db.PrivateMessages.AsNoTracking()
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