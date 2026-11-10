namespace MuhasebeTakip2.App.Services;

// Process-local admission gate. It cannot certify that another instance has drained.
public sealed class ReleaseMaintenance
{
    private readonly object gate = new();
    private readonly bool startupClosed;
    private readonly string controlPath;
    private int active;
    private readonly string instance = Guid.NewGuid().ToString("N");

    public ReleaseMaintenance(IConfiguration config, IWebHostEnvironment env)
    {
        startupClosed = ReadFlag(config, "Maintenance:Enabled");
        controlPath = Path.Combine(env.ContentRootPath, "App_Data", "release-maintenance.control");
    }

    public static bool ReadFlag(IConfiguration config, string name)
    {
        var value = config[name];
        if (value == null) return false;
        if (!bool.TryParse(value, out var enabled)) throw new InvalidOperationException("Geçersiz yayın güvenliği ayarı.");
        return enabled;
    }

    public static void ValidateStartup(IConfiguration config, IWebHostEnvironment env)
    {
        if (!env.IsProduction()) return;
        if (ReadFlag(config, "Database:AutoMigrate") ||
            ReadFlag(config, "Maintenance:AttachLegacyDataToFirstFirm") ||
            ReadFlag(config, "Maintenance:FixDefaultAdminOnStartup"))
            throw new InvalidOperationException("Bu yayın adayı Production başlangıcında migration veya veri düzeltmesi çalıştıramaz.");
    }

    private bool Closed()
    {
        // Read a tiny control file on each admission. Invalid/inaccessible files fail closed.
        try
        {
            if (!File.Exists(controlPath))
            {
                // File.Exists returns false for some access errors. Probe explicitly.
                try { using var probe = File.OpenRead(controlPath); return true; }
                catch (FileNotFoundException) { return startupClosed; }
                catch (DirectoryNotFoundException) { return startupClosed; }
            }
            using var reader = new StreamReader(new FileStream(controlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var value = new char[7];
            var count = reader.ReadBlock(value, 0, value.Length);
            var command = new string(value, 0, count).Trim();
            return command != "open" || !reader.EndOfStream;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    public (bool Closed, bool Drained, string Instance) Snapshot()
    {
        lock (gate)
        {
            var closed = Closed();
            return (closed, closed && active == 0, instance);
        }
    }

    public bool TryEnter()
    {
        lock (gate)
        {
            if (Closed()) return false;
            active++;
            return true;
        }
    }

    public void Exit() { lock (gate) { active--; } }
}

public static class ReleaseMaintenanceMiddleware
{
    public static void UseReleaseMaintenance(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            var maintenance = http.RequestServices.GetRequiredService<ReleaseMaintenance>();
            // Only this exact, read-only liveness route bypasses the dynamic request gate.
            if (http.Request.Path.Equals("/health/live", StringComparison.Ordinal) &&
                (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
            {
                var state = maintenance.Snapshot();
                http.Response.Headers.CacheControl = "no-store";
                if (!HttpMethods.IsHead(http.Request.Method))
                    await http.Response.WriteAsJsonAsync(new { status = "live", maintenance = state.Closed, drained = state.Drained, instance = state.Instance });
                return;
            }
            if (!maintenance.TryEnter())
            {
                http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                http.Response.Headers.RetryAfter = "60";
                http.Response.Headers.CacheControl = "no-store";
                await http.Response.WriteAsync("Bakım sürüyor. Lütfen daha sonra tekrar deneyin.");
                return;
            }
            try { await next(); }
            finally { maintenance.Exit(); }
        });
    }
}