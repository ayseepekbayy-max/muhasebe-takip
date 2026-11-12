using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Services;

public interface IAdminPortalAuthorizationService
{
    Task<bool> IsAuthorizedAsync(HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>Authorizes the configured account using current database state, never session roles.</summary>
public sealed class AdminPortalAuthorizationService(
    AppDbContext db,
    IConfiguration configuration) : IAdminPortalAuthorizationService
{
    public async Task<bool> IsAuthorizedAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Parse explicitly so malformed configuration denies access without breaking ERP startup.
        if (!int.TryParse(configuration["AdminPortal:MainAdminUserId"],
                NumberStyles.None, CultureInfo.InvariantCulture, out var mainAdminId) || mainAdminId <= 0)
            return false;

        var session = context.Features.Get<ISessionFeature>()?.Session;
        if (session is null)
            return false;

        await session.LoadAsync(cancellationToken);
        if (session.GetString("DemoMode") == "1" || session.GetString("PrivateMode") == "1")
            return false;

        var userId = session.GetInt32("KullaniciId");
        if (userId is null or <= 0 || userId.Value != mainAdminId)
            return false;

        // No tracking or result caching: revocation/deletion takes effect on the next check.
        return await db.Kullanicilar.AsNoTracking().AnyAsync(user =>
            user.Id == mainAdminId &&
            user.Rol == "SuperAdmin" &&
            user.Firma != null && user.Firma.AktifMi,
            cancellationToken);
    }
}
