using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public static class SwitchedAccountingBoundary
{
    internal const string AttachmentEndpoint = "AccountingAttachment";
    internal const string ChequeEndpoint = "AccountingCheque";
    public static bool IsViewingAnotherCompany(HttpContext http) => AdmittedContext(http) != null;
    public static int? ViewingCompanyId(HttpContext http) =>
        AdmittedContext(http)?.ActiveCompanyId ?? http.Session.GetInt32("FirmaId");
    private static readonly object RestrictedKey = new();
    private static readonly object HomeAdmissionKey = new();
    public static AccountingRequestContext? AdmittedContext(HttpContext http) =>
        http.Items.TryGetValue(RestrictedKey, out var value) ? value as AccountingRequestContext : null;

    internal static AccountingContextStamp? AdmittedStamp(HttpContext http) =>
        AdmittedContext(http)?.Stamp ?? http.Items[HomeAdmissionKey] as AccountingContextStamp;

    // Page identities come from routing. Prefixes and menu visibility grant no access.
    private static readonly IReadOnlyDictionary<string, string[]> PageHandlers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["/AdminPortal/Index"] = ["OnGetAsync", "OnPostSecAsync", "OnPostGeriDonAsync"],
        ["/Kasa/Hareketler"] = ["OnGetAsync", "OnGetDisaAktarAsync"],
        ["/Kasa/Hesaplar"] = ["OnGetAsync"],
        ["/CariKartlar/Index"] = ["OnGetAsync", "OnGetDisaAktarAsync"],
        ["/CariKartlar/Detay/Index"] = ["OnGetAsync"],
        ["/CariKartlar/Ekstre"] = ["OnGetAsync"],
        ["/Faturalar/Index"] = ["OnGetAsync", "OnGetDisaAktarAsync"],
        ["/Faturalar/Detay"] = ["OnGetAsync"],
        ["/Musteriler/Index"] = ["OnGetAsync", "OnGetDisaAktarAsync"],
        ["/Musteriler/Detay/Index"] = ["OnGetAsync"],
        ["/Cekler/Index"] = ["OnGetAsync"],
        ["/Cekler/Detay"] = ["OnGetAsync"],
        ["/Stoklar/Index"] = ["OnGetAsync", "OnGetDisaAktarAsync"],
        ["/Stoklar/Detay/Index"] = ["OnGetAsync"],
        ["/Raporlar"] = ["OnGetAsync", "OnGetDisaAktarAsync"]
    };

    public static void UseSwitchedAccountingBoundary(this WebApplication app) =>
        app.Use(async (http, next) =>
        {
            var ct = http.RequestAborted;
            await http.Session.LoadAsync(ct);
            var userId = http.Session.GetInt32("KullaniciId");
            var company = http.Session.GetInt32("FirmaId");
            // Anonymous and private Panel sessions have no ERP identity or company.
            if (userId == null && company == null)
            {
                await next();
                return;
            }

            var endpoint = http.GetEndpoint();
            var page = endpoint?.Metadata.GetMetadata<PageActionDescriptor>();
            var read = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
            // Always allow the existing logout to clear stale or revoked accounting sessions.
            if (read && IsRootPage(page) && page!.ViewEnginePath == "/Logout")
            {
                await next();
                return;
            }

            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var store = http.RequestServices.GetRequiredService<IAccountingContextStateStore>();
            var switchEnabled = http.RequestServices.GetRequiredService<AccountingCompanySwitchPolicy>().Enabled;
            var requiresBoundary = await store.ExecuteAsync(http.Session.Id, async (state, _) =>
            {
                var home = await db.Kullanicilar.AsNoTracking().Where(x => x.Id == userId)
                    .Select(x => (int?)x.FirmaId).SingleOrDefaultAsync(ct);
                var restricted = home == null || company != home || (state != null && state.CompanyId != home);
                if (!restricted && switchEnabled && state != null)
                    http.Items[HomeAdmissionKey] = new AccountingContextStamp(state.CompanyId, state.Version);
                return restricted;
            }, ct);

            if (!requiresBoundary)
            {
                await next();
                return;
            }

            var permittedPage = IsRootPage(page) && PageHandlers.ContainsKey(page!.ViewEnginePath) &&
                (read || HttpMethods.IsPost(http.Request.Method));
            var isPortal = IsRootPage(page) && page!.ViewEnginePath == "/AdminPortal/Index";
            if (isPortal)
            {
                if (permittedPage && switchEnabled)
                {
                    // The portal revalidates the real Admin and can recover from an inactive target.
                    http.Items[RestrictedKey] = true;
                    await next();
                    return;
                }
                Deny(http);
                return;
            }

            var permittedAttachment = read &&
                endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is AttachmentEndpoint or ChequeEndpoint;
            if (!read || (!permittedPage && !permittedAttachment))
            {
                Deny(http);
                return;
            }

            try
            {
                var contexts = http.RequestServices.GetRequiredService<IAccountingContextService>();
                var current = await contexts.GetCurrentAsync(http, ct);
                if (current.ActiveCompanyId == current.Actor.HomeCompanyId)
                {
                    Deny(http);
                    return;
                }
                http.Items[RestrictedKey] = current;
                http.Response.Headers.CacheControl = "private, no-store";
            }
            catch (AccountingContextDeniedException)
            {
                Deny(http);
                return;
            }

            await next();
        });

    private static bool IsRootPage(PageActionDescriptor? page) => page != null && string.IsNullOrEmpty(page.AreaName);

    private static void Deny(HttpContext http)
    {
        if (http.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
            statusPages.Enabled = false;
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        http.Response.Headers.CacheControl = "no-store";
    }

    public sealed class HandlerFilter : IAsyncPageFilter, IOrderedFilter
    {
        public int Order => int.MinValue;

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;
            if (http.Items.ContainsKey(RestrictedKey))
            {
                var page = context.ActionDescriptor;
                var handler = context.HandlerMethod;
                var supplied = http.Request.Query["handler"];
                // Razor can fall back to an unnamed handler for an unknown handler name.
                var requestedHandlerMatches = supplied.Count == 0 ||
                    (supplied.Count == 1 && string.Equals(supplied[0], handler?.Name ?? "", StringComparison.OrdinalIgnoreCase));
                if (handler == null || !requestedHandlerMatches ||
                    !PageHandlers.TryGetValue(page.ViewEnginePath, out var allowed) || !allowed.Contains(handler.MethodInfo.Name))
                {
                    Deny(http);
                    context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                    return;
                }
            }

            await next();
        }
    }
}
