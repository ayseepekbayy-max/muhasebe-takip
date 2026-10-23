using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages;
using MuhasebeTakip2.App.Services;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
var root = Path.Combine(Path.GetTempPath(), "demo-access-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (var environment in new[] { Environments.Production, Environments.Staging, "Review", Environments.Development })
    {
        var path = Path.Combine(root, environment); Directory.CreateDirectory(path);
        var monitor = new DatabaseGuard();
        var cs = $"Data Source={Path.Combine(path, "synthetic.db")};Pooling=False";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cs).AddInterceptors(monitor).Options;
        var normalPassword = Guid.NewGuid().ToString("N");
        int normalId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var firm = new Firma { FirmaAdi = "Synthetic normal firm" }; db.Add(firm); await db.SaveChangesAsync();
            var normal = new Kullanici { FirmaId = firm.Id, KullaniciAdi = "synthetic-normal", Email = "normal@example.test" };
            normal.Sifre = PasswordHelper.Hash(normal, normalPassword); db.Add(normal); await db.SaveChangesAsync(); normalId = normal.Id;
            // A pre-existing synthetic demo identity makes unintended seeder writes observable.
            var demoFirm = new Firma { FirmaAdi = "Synthetic existing demo" }; db.Add(demoFirm); await db.SaveChangesAsync();
            var demo = new Kullanici { FirmaId = demoFirm.Id, KullaniciAdi = DemoDataSeeder.DemoUserName, Email = DemoDataSeeder.DemoEmail };
            demo.Sifre = PasswordHelper.Hash(demo, Guid.NewGuid().ToString("N")); db.Add(demo);
            db.CariKartlar.Add(new CariKart { FirmaId = firm.Id, Unvan = "Synthetic sentinel" });
            await db.SaveChangesAsync();
        }
        var privatePassword = Guid.NewGuid().ToString("N");
        var privateHasher = new PasswordHasher<PrivatePerson>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment,
            ApplicationName = typeof(LoginModel).Assembly.GetName().Name, ContentRootPath = path });
        // Drop inherited configuration. No production settings, app startup or external services are used.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["FIRMOVA_PRIVATE_USERNAME"] = "synthetic-private",
            ["FIRMOVA_PRIVATE_PASSWORD_HASH_1"] = privateHasher.HashPassword(new("1", "Synthetic One"), privatePassword),
            ["FIRMOVA_PRIVATE_PASSWORD_HASH_2"] = privateHasher.HashPassword(new("2", "Synthetic Two"), Guid.NewGuid().ToString("N")),
            ["FIRMOVA_PRIVATE_PERSON_1"] = "Synthetic One", ["FIRMOVA_PRIVATE_PERSON_2"] = "Synthetic Two"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorPages().AddApplicationPart(typeof(LoginModel).Assembly);
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None);
        builder.Services.AddScoped(_ => new AppDbContext(options));
        builder.Services.AddScoped<PrivateAccessService>();
        builder.Services.AddSingleton<PrivateLoginAttemptLimiter>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<PrivateSessionRenewal>();
        builder.Services.AddScoped<IOdemeBildirimService, OdemeBildirimService>();
        await using var app = builder.Build();
        app.UseRouting(); app.UseSession(); app.MapRazorPages();
        app.MapGet("/__test/session", (HttpContext c) => Results.Json(new {
            UserId = c.Session.GetInt32("KullaniciId"), DemoMode = c.Session.GetString("DemoMode"),
            PrivateMode = c.Session.GetString("PrivateMode"), PrivatePerson = c.Session.GetString("PrivatePerson") }));
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(url) };
        async Task<string> Token()
        {
            var response = await client.GetAsync("/Login");
            Check(response.StatusCode == HttpStatusCode.OK, environment + " Login GET");
            var html = await response.Content.ReadAsStringAsync();
            Check(html.Contains("demo-login-button") == (environment == Environments.Development), environment + " demo button visibility");
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Check(token.Length > 0, environment + " antiforgery token"); return WebUtility.HtmlDecode(token);
        }
        async Task<HttpResponseMessage> Post(string suffix, string token, string? user = null, string? password = null)
        {
            var fields = new Dictionary<string,string> { ["__RequestVerificationToken"] = token };
            if (user != null) fields["KullaniciAdi"] = user;
            if (password != null) fields["Sifre"] = password;
            return await client.PostAsync("/Login" + suffix, new FormUrlEncodedContent(fields));
        }
        var token = await Token();
        if (environment != Environments.Development)
        {
            var before = await Snapshot(cs);
            monitor.Block = true; monitor.Attempts = 0;
            using var response = await Post("?handler=Demo", token);
            monitor.Block = false;
            Check(response.StatusCode == HttpStatusCode.Forbidden, environment + " direct HTTP demo POST returns 403");
            Check(monitor.Attempts == 0, environment + " seeder cannot reach database (zero commands)");
            var after = await Snapshot(cs);
            Check(before == after, environment + " ALL table schemas and row contents unchanged");
            using var session = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/__test/session"));
            Check(session.RootElement.GetProperty("userId").ValueKind == System.Text.Json.JsonValueKind.Null &&
                session.RootElement.GetProperty("demoMode").ValueKind == System.Text.Json.JsonValueKind.Null, environment + " demo grants no session");
        }
        else
        {
            using var response = await Post("?handler=Demo", token);
            Check(response.StatusCode == HttpStatusCode.Redirect, "Development direct demo POST redirects");
            using var session = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/__test/session"));
            Check(session.RootElement.GetProperty("demoMode").GetString() == "1", "Development demo session retained");
            await using var db = new AppDbContext(options);
            Check(await db.Faturalar.AnyAsync() && await db.StokHareketleri.AnyAsync() && await db.KasaHareketleri.AnyAsync(), "Development demo seeding retained");
        }
        // Normal password verification and session must work in both environments.
        using (var invalid = await Post("", await Token(), "synthetic-normal", "invalid-synthetic-password"))
            Check(invalid.StatusCode == HttpStatusCode.OK, environment + " invalid normal login denied");
        using (var valid = await Post("", await Token(), "synthetic-normal", normalPassword))
            Check(valid.StatusCode == HttpStatusCode.Redirect, environment + " normal login redirects");
        using (var session = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/__test/session")))
            Check(session.RootElement.GetProperty("userId").GetInt32() == normalId &&
                session.RootElement.GetProperty("demoMode").ValueKind == System.Text.Json.JsonValueKind.Null, environment + " normal session and demo reset");
        // Existing private login branch is exercised with synthetic credentials only.
        using (var valid = await Post("", await Token(), "synthetic-private", privatePassword))
            Check(valid.StatusCode == HttpStatusCode.Redirect && valid.Headers.Location!.OriginalString.Contains("Panel"), environment + " private login redirects");
        using (var session = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/__test/session")))
            Check(session.RootElement.GetProperty("privateMode").GetString() == "1" &&
                session.RootElement.GetProperty("privatePerson").GetString() == "1" &&
                session.RootElement.GetProperty("userId").ValueKind == System.Text.Json.JsonValueKind.Null, environment + " private session isolation");
        await app.StopAsync();
    }
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
Console.WriteLine($"PASS: {checks} synthetic demo/login HTTP checks; no production database.");

static async Task<string> Snapshot(string cs)
{
    await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
    var names = new List<string>();
    await using (var cmd = connection.CreateCommand())
    { cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
      await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) names.Add(reader.GetString(0)); }
    var data = new StringBuilder();
    foreach (var name in names)
    {
        data.Append(name).Append(':');
        await using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT * FROM \"" + name.Replace("\"", "\"\"") + "\" ORDER BY rowid";
        await using var reader = await cmd.ExecuteReaderAsync();
        for (int i=0;i<reader.FieldCount;i++) data.Append(reader.GetName(i)).Append('|');
        while(await reader.ReadAsync()) for(int i=0;i<reader.FieldCount;i++)
            data.Append(System.Text.Json.JsonSerializer.Serialize(reader.IsDBNull(i)?null:reader.GetValue(i))).Append('|');
    }
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString())));
}
sealed class DatabaseGuard : DbCommandInterceptor
{
    public bool Block; public int Attempts;
    void Guard() { if(Block) { Interlocked.Increment(ref Attempts); throw new InvalidOperationException("Non-development demo attempted database access"); } }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken ct=default) { Guard(); return ValueTask.FromResult(r); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<int> r, CancellationToken ct=default) { Guard(); return ValueTask.FromResult(r); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<object> r, CancellationToken ct=default) { Guard(); return ValueTask.FromResult(r); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r) { Guard(); return r; }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData d, InterceptionResult<int> r) { Guard(); return r; }
    public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData d, InterceptionResult<object> r) { Guard(); return r; }
}
