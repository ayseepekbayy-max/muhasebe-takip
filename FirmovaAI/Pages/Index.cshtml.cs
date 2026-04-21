using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FirmovaAI.Services;
using System.Text.Json;

namespace FirmovaAI.Pages;

public class IndexModel : PageModel
{
    private readonly OpenAiService _ai;

    public IndexModel(OpenAiService ai)
    {
        _ai = ai;
    }

    [BindProperty]
    public string Soru { get; set; } = "";

    public List<Mesaj> Mesajlar { get; set; } = new();

    public void OnGet()
    {
        Mesajlar = SessiondanMesajlariGetir();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Mesajlar = SessiondanMesajlariGetir();

        if (string.IsNullOrWhiteSpace(Soru))
            return Page();

        var cevap = await _ai.SorAsync(Soru);

        Mesajlar.Add(new Mesaj
        {
            Tip = "user",
            Metin = Soru,
            Tarih = DateTime.Now
        });

        Mesajlar.Add(new Mesaj
        {
            Tip = "ai",
            Metin = cevap,
            Tarih = DateTime.Now
        });

        SessionaMesajlariKaydet(Mesajlar);

        Soru = "";
        return Page();
    }

    public IActionResult OnPostTemizle()
    {
        HttpContext.Session.Remove("chat_gecmisi");
        return RedirectToPage();
    }

    private List<Mesaj> SessiondanMesajlariGetir()
    {
        var json = HttpContext.Session.GetString("chat_gecmisi");

        if (string.IsNullOrWhiteSpace(json))
            return new List<Mesaj>();

        return JsonSerializer.Deserialize<List<Mesaj>>(json) ?? new List<Mesaj>();
    }

    private void SessionaMesajlariKaydet(List<Mesaj> mesajlar)
    {
        var json = JsonSerializer.Serialize(mesajlar);
        HttpContext.Session.SetString("chat_gecmisi", json);
    }

    public class Mesaj
    {
        public string Tip { get; set; } = "";
        public string Metin { get; set; } = "";
        public DateTime Tarih { get; set; }
    }
}