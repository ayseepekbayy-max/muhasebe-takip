using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MuhasebeTakip2.App.Services;

/// <summary>Apply to future portal PageModels via ServiceFilter; not to existing SuperAdmin pages.</summary>
public sealed class AdminPortalAuthorizationFilter(
    IAdminPortalAuthorizationService authorization) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!await authorization.IsAuthorizedAsync(context.HttpContext, context.HttpContext.RequestAborted))
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}

public static class AdminPortalAuthorizationRegistration
{
    public static IServiceCollection AddAdminPortalAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAdminPortalAuthorizationService, AdminPortalAuthorizationService>();
        services.AddScoped<AdminPortalAuthorizationFilter>();
        return services;
    }
}
