using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FirmovaAI.Models.Ai;
using FirmovaAI.Services.Ai;
using System.Text.Json;

namespace FirmovaAI.Pages.Ask;

public class IndexModel : PageModel
{
    private readonly QueryInterpreter _interpreter;
    private readonly QueryExecutor _executor;

    public IndexModel(QueryInterpreter interpreter, QueryExecutor executor)
    {
        _interpreter = interpreter;
        _executor = executor;
    }

    [BindProperty]
    public string Soru { get; set; } = "";

    public string Cevap { get; set; } = "";

    public List<ChatMessage> Mesajlar { get; set; } = new();

    public void OnGet()
    {
        Mesajlar = GetMessages();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Mesajlar = GetMessages();

        if (string.IsNullOrWhiteSpace(Soru))
            return Page();

        Mesajlar.Add(new ChatMessage
        {
            Role = "user",
            Text = Soru
        });

        try
        {
            var sonuc = _interpreter.Interpret(Soru);
            Cevap = await _executor.ExecuteAsync(sonuc);
        }
        catch (Exception ex)
        {
            Cevap =
                "İşlem sırasında hata oluştu.\n\n" +
                $"Hata: {ex.Message}\n\n" +
                $"Detay: {ex.InnerException?.Message}";
        }

        Mesajlar.Add(new ChatMessage
        {
            Role = "bot",
            Text = Cevap
        });

        SaveMessages(Mesajlar);

        return Page();
    }

    public IActionResult OnPostTemizle()
    {
        HttpContext.Session.Remove("FirmovaChatHistory");
        return RedirectToPage();
    }

    private List<ChatMessage> GetMessages()
    {
        var json = HttpContext.Session.GetString("FirmovaChatHistory");

        if (string.IsNullOrWhiteSpace(json))
            return new List<ChatMessage>();

        try
        {
            return JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new List<ChatMessage>();
        }
        catch
        {
            HttpContext.Session.Remove("FirmovaChatHistory");
            return new List<ChatMessage>();
        }
    }

    private void SaveMessages(List<ChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages);
        HttpContext.Session.SetString("FirmovaChatHistory", json);
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
}