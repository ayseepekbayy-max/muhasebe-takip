using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FirmovaAI.Models.Ai;
using FirmovaAI.Services.Ai;

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

    public QueryIntent? Sonuc { get; set; }
    public string Cevap { get; set; } = "";

    public void OnGet()
    {
    }

    public async Task OnPostAsync()
    {
        Sonuc = _interpreter.Interpret(Soru);
        Cevap = await _executor.ExecuteAsync(Sonuc);
    }
}