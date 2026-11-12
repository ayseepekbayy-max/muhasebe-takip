using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MuhasebeTakip2.App.Services;

public enum AccountingManagementPermission
{
    PersonalAccount,
    CompanySettings,
    BackupRead,
    BackupRestore
}

public sealed class AccountingManagementAccess(IAccountingContextService contexts)
{
    private static readonly object PersonalAuditKey = new();

    public static bool CanManageCompany(AccountingRequestContext context) =>
        context.ActiveCompanyId == context.Actor.HomeCompanyId;

    internal static AccountingActor? PersonalAuditActor(HttpContext? http) =>
        http?.Items.TryGetValue(PersonalAuditKey, out var value) == true ? value as AccountingActor : null;

    public async Task<IActionResult> ReadAsync(PageModel page, AccountingManagementPermission permission,
        Func<AccountingRequestContext, Task<IActionResult>> operation)
    {
        try
        {
            var context = await contexts.GetCurrentAsync(page.HttpContext, page.HttpContext.RequestAborted);
            return await RunAsync(page, permission, context, operation);
        }
        catch (AccountingContextDeniedException)
        {
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }

    public async Task<IActionResult> SubmitAsync(PageModel page, AccountingManagementPermission permission,
        Func<AccountingRequestContext, Task<IActionResult>> operation)
    {
        var http = page.HttpContext;
        var ct = http.RequestAborted;
        if (!http.Request.HasFormContentType) return new StatusCodeResult(StatusCodes.Status409Conflict);
        var form = await http.Request.ReadFormAsync(ct);
        var companies = form["AccountingCompanyId"];
        var versions = form["AccountingVersion"];
        if (companies.Count != 1 || versions.Count != 1 ||
            !int.TryParse(companies[0], NumberStyles.None, CultureInfo.InvariantCulture, out var company) ||
            !Guid.TryParseExact(versions[0], "D", out var version))
            return new StatusCodeResult(StatusCodes.Status409Conflict);

        try
        {
            return await contexts.WithValidatedWriteAsync(http, new AccountingContextStamp(company, version),
                (context, _) => RunAsync(page, permission, context, operation), ct);
        }
        catch (AccountingContextDeniedException)
        {
            return new StatusCodeResult(StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IActionResult> RunAsync(PageModel page, AccountingManagementPermission permission,
        AccountingRequestContext context, Func<AccountingRequestContext, Task<IActionResult>> operation)
    {
        var allowed = permission switch
        {
            AccountingManagementPermission.PersonalAccount => true,
            AccountingManagementPermission.CompanySettings or AccountingManagementPermission.BackupRead or
                AccountingManagementPermission.BackupRestore => CanManageCompany(context),
            _ => false
        };
        var http = page.HttpContext;
        http.Response.Headers.CacheControl = "private, no-store";
        if (!allowed) return new StatusCodeResult(StatusCodes.Status403Forbidden);

        page.ViewData["ManagementContextStamp"] = context.Stamp;
        page.ViewData["CanManageCompany"] = CanManageCompany(context);
        if (permission == AccountingManagementPermission.PersonalAccount)
            http.Items[PersonalAuditKey] = context.Actor;

        try { return await operation(context); }
        finally { http.Items.Remove(PersonalAuditKey); }
    }
}
