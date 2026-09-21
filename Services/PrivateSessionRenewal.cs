using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Options;

namespace MuhasebeTakip2.App.Services;

public sealed class PrivateSessionRenewal(IServiceProvider services, IOptions<SessionOptions> options)
{
    public async Task SignInAsync(HttpContext context, PrivatePerson person)
    {
        var previousFeature = context.Features.Get<ISessionFeature>();
        var originalCookieHeader = context.Request.Headers.Cookie;
        var remainingCookies = context.Request.Cookies
            .Where(cookie => cookie.Key != options.Value.Cookie.Name)
            .Select(cookie => cookie.Key + "=" + cookie.Value).ToArray();

        // Invalidate the old store entry before granting access to a new one.
        context.Session.Clear();
        await context.Session.CommitAsync(context.RequestAborted);
        try
        {
            // With no incoming session cookie, the framework generates and protects
            // a fresh session key. Never construct a cookie or authentication token here.
            context.Request.Headers.Cookie = string.Join("; ", remainingCookies);
            RequestDelegate establish = async current =>
            {
                current.Session.SetString("PrivateMode", "1");
                current.Session.SetString("PrivatePerson", person.Number);
                current.Session.SetString("PrivatePersonName", person.Name);
                // Surface persistence failures instead of reporting a successful login.
                await current.Session.CommitAsync(current.RequestAborted);
            };
            var middleware = ActivatorUtilities.CreateInstance<SessionMiddleware>(services, establish);
            await middleware.Invoke(context);
        }
        finally
        {
            context.Request.Headers.Cookie = originalCookieHeader;
            context.Features.Set(previousFeature);
        }
    }
}
