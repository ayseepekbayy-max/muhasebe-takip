using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages;
using MuhasebeTakip2.App.Services;

if(args.Length!=0)throw new Exception("Synthetic only.");
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
await using var lab=await TemporaryPostgres.StartAsync();
await lab.InitializeAsync();
var policy=new Policy();var logs=new Logs();var dbOptions=lab.Options;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",ApplicationName=typeof(PanelModel).Assembly.GetName().Name});
builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.WebHost.UseTestServer();builder.Logging.ClearProviders();builder.Logging.AddProvider(logs);
builder.Services.AddRazorPages();builder.Services.AddDistributedMemoryCache();builder.Services.AddSession();builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton(TimeProvider.System);builder.Services.AddSingleton<IOptionsMonitor<PrivateMediaOptions>>(policy);builder.Services.AddScoped(_=>new AppDbContext(dbOptions));builder.Services.AddScoped<PrivateMediaStore>();builder.Services.AddSingleton<PrivateLoginAttemptLimiter>();builder.Services.AddScoped<PrivateAccessService>();
await using var app=builder.Build();app.UseRouting();app.UseSession();app.UseAntiforgery();
app.MapGet("/synthetic-session",(HttpContext c,IAntiforgery anti)=>{c.Session.SetString("PrivateMode","1");c.Session.SetString("PrivatePerson","1");c.Session.SetString("PrivatePersonName","SYNTHETIC_NAME_NEVER_LOG");return Results.Json(new{token=anti.GetAndStoreTokens(c).RequestToken});});app.MapRazorPages();await app.StartAsync();
using var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");
var session=await client.GetAsync("/synthetic-session");client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",session.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
var token=(await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
var photo=new byte[64];photo[0]=255;photo[1]=216;photo[2]=255;photo[^2]=255;photo[^1]=217;
async Task<HttpResponseMessage> Send(bool csrf=true,string kind="photo"){
 using var form=new MultipartFormDataContent();if(csrf)form.Add(new StringContent(token),"__RequestVerificationToken");form.Add(new StringContent(kind),"kind");
 var payload=photo;
 if(kind=="audio"){payload=new byte[80];"OggS"u8.CopyTo(payload);"OpusHead"u8.CopyTo(payload.AsSpan(20));form.Add(new StringContent("2"),"durationSeconds");}
 form.Add(new ByteArrayContent(payload),"file","SYNTHETIC_FILE_NEVER_LOG.bin");return await client.PostAsync("/Panel?handler=Media",form);
}
bool renamed=false;
async Task<bool> Empty(){await using var d=new AppDbContext(lab.Options);var payloads=renamed?await d.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM public.\"SyntheticUnavailable\"").SingleAsync():await d.PrivateMediaContents.LongCountAsync();return !await d.PrivateMessages.AnyAsync()&&payloads==0;}
async Task Failure(string code){logs.Clear();var r=await Send();Check(r.StatusCode==HttpStatusCode.ServiceUnavailable,"503 for "+code);var text=await r.Content.ReadAsStringAsync();Check(text.Contains("Medya")&&!text.Contains(code),"public response remains generic");Check(logs.Text.Contains(code),"safe diagnostic "+code);Check(await Empty(),"failed upload leaves no orphan message/payload");}
policy.Value=new(){BlockNewUploads=true,MaxDatabaseBytes=1000000000,DatabaseReserveBytes=268435456};await Failure("MEDIA_UPLOADS_BLOCKED");
policy.Value=new(){BlockNewUploads=false};await Failure("MEDIA_CAPACITY_CONFIG");
policy.Value=new(){MaxDatabaseBytes=1};await Failure("MEDIA_CAPACITY_HEADROOM");
policy.Value=new(){MaxDatabaseBytes=1000000000,DatabaseReserveBytes=268435456};
logs.Clear();var noCsrf=await Send(false);Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"antiforgery fails before handler with 400");Check(!logs.Text.Contains("Private media upload stopped")&&await Empty(),"CSRF rejection performs no upload write");
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"PrivateMediaContents\" RENAME TO \"SyntheticUnavailable\"");}
renamed=true;
await Failure("MEDIA_DB_SCHEMA");
Check(logs.Text.Contains("Stage=PAYLOAD_INSERT")&&logs.Text.Contains("ContentsInsert=False"),"missing table identified at payload stage without record contents");
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"SyntheticUnavailable\" RENAME TO \"PrivateMediaContents\"");}
renamed=false;
// Separate runtime permissions from the schema owner, using synthetic credentials only.
var limited=new Npgsql.NpgsqlConnectionStringBuilder(lab.ConnectionString);
await using(var c=new Npgsql.NpgsqlConnection(lab.ConnectionString)){
 await c.OpenAsync();await using var cmd=new Npgsql.NpgsqlCommand("CREATE ROLE synthetic_runtime LOGIN PASSWORD '"+limited.Password+"'; GRANT USAGE ON SCHEMA public TO synthetic_runtime; GRANT SELECT,INSERT ON public.\"PrivateMessages\" TO synthetic_runtime; GRANT SELECT ON public.\"PrivateMediaContents\",public.\"PrivateMediaQuotas\" TO synthetic_runtime; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO synthetic_runtime",c);await cmd.ExecuteNonQueryAsync();
}
limited.Username="synthetic_runtime";dbOptions=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(limited.ConnectionString).Options;
await Failure("MEDIA_DB_PERMISSION");
Check(logs.Text.Contains("Stage=PAYLOAD_INSERT")&&logs.Text.Contains("ContentsInsert=False")&&logs.Text.Contains("QuotaUpdate=False"),"actual runtime privileges distinguish missing media INSERT");
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("GRANT INSERT ON public.\"PrivateMediaContents\" TO synthetic_runtime");}
await Failure("MEDIA_DB_PERMISSION"); // Trigger requires quota UPDATE as well as media INSERT.
Check(logs.Text.Contains("ContentsInsert=True")&&logs.Text.Contains("QuotaUpdate=False"),"actual runtime privileges distinguish missing quota column UPDATE");
await using(var c=new Npgsql.NpgsqlConnection(limited.ConnectionString)){
 await c.OpenAsync();await using var cmd=new Npgsql.NpgsqlCommand("SELECT oid::bigint FROM pg_catalog.pg_roles WHERE rolname=current_user",c);
 var roleOid=(long)(await cmd.ExecuteScalarAsync())!;
 Check(logs.Text.Contains("RoleOid="+roleOid+";"),"diagnostic identifies effective database role rather than control connection role");
}
dbOptions=lab.Options;
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("UPDATE public.\"PrivateMediaQuotas\" SET \"UsedBytes\"=104857600");}
Check((await Send()).StatusCode==(HttpStatusCode)507,"full logical quota returns distinct 507");Check(await Empty(),"full quota rolls back message and payload");
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("UPDATE public.\"PrivateMediaQuotas\" SET \"UsedBytes\"=0");}
// Repair only the missing column privilege, without ownership, DDL, or table-wide UPDATE.
await using(var d=new AppDbContext(lab.Options)){await d.Database.ExecuteSqlRawAsync("GRANT UPDATE (\"UsedBytes\") ON TABLE public.\"PrivateMediaQuotas\" TO synthetic_runtime");}
dbOptions=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(limited.ConnectionString).Options;
await using(var c=new Npgsql.NpgsqlConnection(limited.ConnectionString)){
 await c.OpenAsync();await using var cmd=new Npgsql.NpgsqlCommand("SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole AND NOT rolcreatedb AND NOT pg_catalog.has_schema_privilege('public','CREATE') AND NOT pg_catalog.has_table_privilege('public.\"PrivateMediaContents\"','UPDATE') AND NOT pg_catalog.has_column_privilege('public.\"PrivateMediaQuotas\"','Id','UPDATE') AND pg_catalog.has_column_privilege('public.\"PrivateMediaQuotas\"','UsedBytes','UPDATE') FROM pg_catalog.pg_roles WHERE rolname=current_user",c);
 Check(await cmd.ExecuteScalarAsync() is true,"minimal repair retains restricted role and column-only quota UPDATE");
}
Check((await Send()).IsSuccessStatusCode,"exact production capacity settings permit synthetic photo");
await using(var d=new AppDbContext(lab.Options)){Check(await d.PrivateMessages.CountAsync()==1&&await d.PrivateMediaContents.CountAsync()==1,"message and payload committed together");Check(await d.PrivateMediaQuotas.Select(x=>x.UsedBytes).SingleAsync()==64,"quota trigger counts payload exactly");}
Check((await Send(kind:"audio")).IsSuccessStatusCode,"minimal privileges also permit synthetic audio");
await using(var d=new AppDbContext(lab.Options)){Check(await d.PrivateMessages.CountAsync()==2&&await d.PrivateMediaContents.CountAsync()==2,"both uploads committed without orphan records");Check(await d.PrivateMediaQuotas.Select(x=>x.UsedBytes).SingleAsync()==144,"photo and audio quota totals exact");}
Check(!logs.Text.Contains("SYNTHETIC_NAME_NEVER_LOG")&&!logs.Text.Contains("SYNTHETIC_FILE_NEVER_LOG"),"no personal name or uploaded filename in logs");
Check(!logs.Text.Contains(limited.Password!)&&!logs.Text.Contains("synthetic_runtime"),"diagnostics omit credentials and raw role name");
// Verify environment-style configuration binding without reading process environment.
var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"PrivateMedia:BlockNewUploads","false"},{"PrivateMedia:MaxDatabaseBytes","1000000000"},{"PrivateMedia:DatabaseReserveBytes","268435456"}}).Build();var bound=configuration.GetSection("PrivateMedia").Get<PrivateMediaOptions>()!;
Check(!bound.BlockNewUploads&&bound.MaxDatabaseBytes==1000000000&&bound.DatabaseReserveBytes==268435456,"configuration binds false and long capacity values correctly");
Console.WriteLine($"PASS: {checks}/{checks} media diagnostics; synthetic PostgreSQL 18.6 only.");
sealed class Policy:IOptionsMonitor<PrivateMediaOptions>{public PrivateMediaOptions Value=new();public PrivateMediaOptions CurrentValue=>Value;public PrivateMediaOptions Get(string? name)=>Value;public IDisposable? OnChange(Action<PrivateMediaOptions,string?> listener)=>null;}
sealed class Logs:ILoggerProvider{
 readonly System.Collections.Concurrent.ConcurrentQueue<string> entries=new();public string Text=>string.Join("\n",entries);public void Clear()=>entries.Clear();public void Dispose(){}public ILogger CreateLogger(string category)=>new Capture(this);
 sealed class Capture(Logs owner):ILogger{public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)=>owner.entries.Enqueue(format(state,error));}
}
