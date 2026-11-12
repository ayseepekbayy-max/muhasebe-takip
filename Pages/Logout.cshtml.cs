using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

public class LogoutModel(AccountingSessionLifecycle sessionLifecycle) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        await sessionLifecycle.EndAsync(HttpContext);
        return RedirectToPage("/Login");
    }
}
