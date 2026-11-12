using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public class AccountingPageContextFilter(IAccountingContextService contexts, AppDbContext db) : IAsyncPageFilter
{
    private static readonly object Key = new();
    protected AppDbContext Db { get; } = db;

    public static AccountingRequestContext? Current(HttpContext http) =>
        http.Items.TryGetValue(Key, out var value) ? value as AccountingRequestContext : null;

    public static int CompanyId(HttpContext http) =>
        Current(http)?.ActiveCompanyId ?? (int)http.Items[Key]!;

    internal static async Task<int?> VerifiedDemoCompanyAsync(AppDbContext db, HttpContext http)
    {
        var user = http.Session.GetInt32("KullaniciId");
        var company = http.Session.GetInt32("FirmaId");
        if (http.Session.GetString("PrivateMode") == "1" || user is null or <= 0 || company is null or <= 0)
            return null;

        return await db.Kullanicilar.AsNoTracking().AnyAsync(x => x.Id == user && x.Rol == "Demo" &&
            x.FirmaId == company && x.Firma != null && x.Firma.AktifMi, http.RequestAborted)
            ? company : null;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        var read = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
        PageHandlerExecutedContext? executed = null;
        try
        {
            if (read)
            {
                if (http.Session.GetString("DemoMode") == "1")
                    http.Items[Key] = await VerifiedDemoCompanyAsync(Db, http) ?? throw new AccountingContextDeniedException();
                else
                    Publish(await contexts.GetCurrentAsync(http, http.RequestAborted));
                await ExecuteAsync();
                return;
            }

            if (http.Session.GetString("DemoMode") == "1" || http.Session.GetString("PrivateMode") == "1")
            {
                Reject(403);
                return;
            }

            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            if (form["AccountingCompanyId"].Count != 1 || form["AccountingVersion"].Count != 1 ||
                !int.TryParse(form["AccountingCompanyId"], NumberStyles.None, CultureInfo.InvariantCulture, out var company) ||
                !Guid.TryParse(form["AccountingVersion"], out var version))
            {
                Reject(409);
                return;
            }

            await contexts.WithValidatedWriteAsync(http, new(company, version), async (current, _) =>
            {
                Publish(current);
                return await RunWriteAsync(context, ExecuteAsync);
            }, http.RequestAborted);
        }
        catch (AccountingContextDeniedException) { Reject(read ? 403 : 409); }
        catch (Exception ex) when (InvoicePaymentTransaction.IsConflict(ex)) { Reject(409); }
        finally { http.Items.Remove(Key); }

        async Task<PageHandlerExecutedContext> ExecuteAsync()
        {
            executed = await next();
            if (executed.Exception is { } error && !executed.ExceptionHandled)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return executed;
        }

        void Publish(AccountingRequestContext current)
        {
            http.Items[Key] = current;
            ((PageModel)context.HandlerInstance).ViewData["AccountingContextStamp"] = current.Stamp;
        }

        void Reject(int status)
        {
            var result = new StatusCodeResult(status);
            context.Result = result;
            if (executed != null)
            {
                executed.ExceptionHandled = true;
                executed.Result = result;
            }
        }
    }

    protected virtual Task<PageHandlerExecutedContext> RunWriteAsync(PageHandlerExecutingContext context,
        Func<Task<PageHandlerExecutedContext>> execute) => execute();
}
