using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MuhasebeTakip2.App.Services;

public static class AccountingSwitchTokens
{
    private const string Purpose = "Firmova.AdminPortal.CompanyTransition.v1";

    internal sealed record Ticket(Guid Id, int UserId, string Epoch, int Source, Guid Version, int Target, bool ReturnHome, Guid SwitchGeneration);

    public static string Issue(HttpContext http, AccountingRequestContext context, int? target = null)
    {
        var epoch = http.Session.GetString(AccountingSessionLifecycle.EpochKey);
        if (string.IsNullOrWhiteSpace(epoch) || context.SwitchGeneration is not Guid generation)
            throw new AccountingContextDeniedException();

        var ticket = new Ticket(Guid.NewGuid(), context.Actor.UserId, epoch, context.ActiveCompanyId,
            context.Version, target ?? context.Actor.HomeCompanyId, !target.HasValue, generation);
        return Protector(http).Protect(JsonSerializer.Serialize(ticket));
    }

    internal static Ticket? Read(HttpContext http, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048) return null;
        try
        {
            return JsonSerializer.Deserialize<Ticket>(Protector(http).Unprotect(token));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static IDataProtector Protector(HttpContext http) =>
        http.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
}
