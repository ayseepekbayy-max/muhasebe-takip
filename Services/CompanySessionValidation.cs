using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public static class CompanySessionValidation
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var companyId = context.Session.GetInt32("FirmaId");
        var userId = context.Session.GetInt32("KullaniciId");
        if (companyId.HasValue && context.Session.GetString("PrivateMode") != "1")
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            if (context.Session.GetString("DemoMode") == "1" &&
                await AccountingPageContextFilter.VerifiedDemoCompanyAsync(db, context) is not null)
            {
                await next(context);
                return;
            }
            if (!userId.HasValue || !await db.Kullanicilar.AsNoTracking().AnyAsync(x =>
                    x.Id == userId && x.FirmaId == companyId && x.Firma != null && x.Firma.AktifMi,
                    context.RequestAborted))
            {
                context.Session.Clear();
                if (context.Request.Path.StartsWithSegments("/api"))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    context.Response.Redirect("/Login");
                return;
            }
        }
        await next(context);
    }
}
