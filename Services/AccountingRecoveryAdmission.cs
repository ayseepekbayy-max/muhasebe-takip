using System.Diagnostics;

namespace MuhasebeTakip2.App.Services;

/// <summary>Admit backup operations before MVC/antiforgery can buffer their request bodies.</summary>
public static class AccountingRecoveryAdmission
{
    public const long MaxRequestBytes = AccountingBackupService.MaxPackageBytes + 1024 * 1024;
    private const long ReserveBytes = 192L * 1024 * 1024;

    public static void UseAccountingRecoveryAdmission(this WebApplication app)
    {
        var gate = new SemaphoreSlim(1, 1);
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method) ||
                !string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/Araclar/Yedekleme", StringComparison.OrdinalIgnoreCase))
            { await next(); return; }
            if (context.Request.ContentLength > MaxRequestBytes)
            { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
            if (!gate.Wait(0))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "5";
                return;
            }
            try
            {
                // No waiting queue holding uploaded bodies. Unknown-length requests reserve the full limit.
                var length = context.Request.ContentLength ?? MaxRequestBytes;
                var encryptedUpload = context.Request.Query["handler"].Any(value =>
                    string.Equals(value, "SifreliDosyaGeriYukle", StringComparison.OrdinalIgnoreCase));
                // Existing small ZIP/JSON actions and downloads retain their normal behavior.
                if ((encryptedUpload || length > 64 * 1024) && !HasMemoryBudget(length))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.RetryAfter = "5";
                    await context.Response.WriteAsync("Yedekleme işlemi için yeterli bellek yok. Daha sonra tekrar deneyin.");
                    return;
                }
                var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaxRequestBytes;
                await next();
            }
            finally { gate.Release(); }
        });
    }

    private static bool HasMemoryBudget(long length)
        => length is >= 0 and <= MaxRequestBytes && FitsMemory(length * 5);

    public static bool HasRestoreMemoryBudget(long packageBytes, long manifestBytes) =>
        packageBytes is >= 0 and <= AccountingBackupService.MaxPackageBytes &&
        manifestBytes is >= 0 and <= AccountingBackupService.MaxManifestBytes &&
        FitsMemory(packageBytes * 2 + manifestBytes * 32);

    private static bool FitsMemory(long additionalBytes)
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        using var process = Process.GetCurrentProcess();
        var used = Math.Max(process.WorkingSet64, GC.GetTotalMemory(false));
        if (OperatingSystem.IsLinux())
        {
            try
            {
                if (long.TryParse(File.ReadAllText("/sys/fs/cgroup/memory.current").Trim(), out var current))
                    used = Math.Max(used, current);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return available > used && available - used > ReserveBytes + additionalBytes;
    }
}
