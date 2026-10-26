using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages;
using MuhasebeTakip2.App.Services;

if(args.SequenceEqual(new[]{"--write-migration-script"})) {
    // Offline SQL generation only: no application startup or database connection.
    await using var scriptDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=unused_offline_script;Username=unused").Options);
    var sql=scriptDb.GetService<IMigrator>().GenerateScript(
        "20261020103737_AddCashBankAccounts", "20261024104204_AddPostgresPrivateMedia");
    var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Docs/AddPostgresPrivateMedia.review.sql"));
    await File.WriteAllTextAsync(path,sql);
    Console.WriteLine("Review SQL generated offline; no database connection.");return;
}
if(args.Length!=0)throw new ArgumentException("No external database or configuration arguments accepted.");
var root=Path.Combine(Path.GetTempPath(),"pg-media-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var clock=new TestClock(DateTime.UtcNow);var policy=new MutableOptions();var logs=new LogCapture();
WebApplication? app=null;TemporaryPostgres? lab=null;
byte[] Synthetic(bool photo,int length) {
    var bytes=new byte[length];new Random(42).NextBytes(bytes);
    if(photo){bytes[0]=255;bytes[1]=216;bytes[2]=255;bytes[^2]=255;bytes[^1]=217;}
    else {"OggS"u8.CopyTo(bytes);"OpusHead"u8.CopyTo(bytes.AsSpan(20));}
    "SYNTHETIC_MEDIA_DO_NOT_LOG"u8.CopyTo(bytes.AsSpan(photo?4:36));return bytes;
}
var jpeg=Synthetic(true,64);var audio=Synthetic(false,80);
try {
    lab=await TemporaryPostgres.StartAsync();var opts=lab.Options;int firmaId=0,cariId=0;string legacyKey=Guid.NewGuid().ToString("N");
    await lab.InitializeAsync(async db=>{
        var f=new Firma{FirmaAdi="Synthetic preserved company"};db.Add(f);await db.SaveChangesAsync();firmaId=f.Id;
        var c=new CariKart{FirmaId=f.Id,Unvan="Synthetic preserved account",OlusturmaTarihi=DateTime.UtcNow};db.Add(c);await db.SaveChangesAsync();cariId=c.Id;
        var at=DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PrivateMessages" ("Id","SenderPerson","SenderName","Content","Kind","CreatedAtUtc","IsReply","ReplyToMessageId","MediaKey","MediaContentType","PhotoViewHours") VALUES
            (-1,1,'Synthetic','Preserved text',0,{at},false,NULL,NULL,NULL,NULL),
            (-2,1,'Synthetic','',2,{at},false,NULL,{legacyKey},'image/jpeg',3),
            (-3,2,'Synthetic','Preserved reply',0,{at},true,-1,NULL,NULL,NULL)
            """);
    },Check);
    await using(var db=new AppDbContext(opts)) {
        Check(await db.Firmalar.AnyAsync(x=>x.Id==firmaId&&x.FirmaAdi=="Synthetic preserved company")&&await db.CariKartlar.AnyAsync(x=>x.Id==cariId&&x.FirmaId==firmaId),"Migration preserves accounting records and relationships");
        Check(await db.PrivateMessages.AnyAsync(x=>x.Id==-2&&x.MediaKey==legacyKey&&x.PhotoViewHours==3)&&await db.PrivateMessages.AnyAsync(x=>x.Id==-3&&x.ReplyToMessageId==-1&&x.IsReply),"Migration preserves legacy media keys, text and replies");
        Check(await db.PrivateMediaContents.CountAsync()==0&&await db.PrivateMediaQuotas.Select(x=>x.UsedBytes).SingleAsync()==0,"Empty bytea store does not import or inspect legacy media");
        var catalog=await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM information_schema.columns
            WHERE table_schema='public' AND table_name='PrivateMediaContents' AND column_name='Payload' AND data_type='bytea' AND is_nullable='NO'
            """).SingleAsync();Check(catalog==1,"Actual PostgreSQL bytea column verified");
        var vacuum=await db.Database.SqlQueryRaw<bool>("SELECT 'autovacuum_vacuum_threshold=1'=ANY(reloptions) AS \"Value\" FROM pg_class WHERE oid='\"PrivateMediaContents\"'::regclass").SingleAsync();Check(vacuum,"Autovacuum tuned only for new media table");
        Check(await db.Database.SqlQueryRaw<string>("SELECT relpersistence::text AS \"Value\" FROM pg_class WHERE oid='\"PrivateMediaContents\"'::regclass").SingleAsync()=="p","Media table is permanent/WAL logged, not temporary or unlogged");
    }
    async Task<WebApplication> Start(bool automaticCleanup=false) {
        var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",ContentRootPath=root,ApplicationName=typeof(PanelModel).Assembly.GetName().Name});
        b.Configuration.Sources.Clear();b.Configuration.AddInMemoryCollection();b.WebHost.UseTestServer();b.Logging.ClearProviders();b.Logging.AddProvider(logs);
        b.Services.AddRazorPages();b.Services.AddDistributedMemoryCache();b.Services.AddSession(o=>{o.Cookie.SecurePolicy=CookieSecurePolicy.Always;o.Cookie.HttpOnly=true;});
        b.Services.AddDataProtection().UseEphemeralDataProtectionProvider();b.Services.AddSingleton<TimeProvider>(clock);
        b.Services.AddSingleton<IOptionsMonitor<PrivateMediaOptions>>(policy);b.Services.AddScoped(_=>new AppDbContext(opts));b.Services.AddScoped<PrivateMediaStore>();
        b.Services.AddSingleton<PrivateLoginAttemptLimiter>();b.Services.AddScoped<PrivateAccessService>();
        if(automaticCleanup)b.Services.AddHostedService<PrivateMediaCleanupService>();
        var host=b.Build();host.UseRouting();host.UseSession();host.UseAntiforgery();
        host.MapGet("/synthetic-session/{person:int}",(int person,HttpContext ctx,IAntiforgery anti)=>{
            ctx.Session.SetString("PrivateMode","1");ctx.Session.SetString("PrivatePerson",person.ToString());ctx.Session.SetString("PrivatePersonName","Synthetic");
            return Results.Json(new{token=anti.GetAndStoreTokens(ctx).RequestToken});
        });host.MapRazorPages();await host.StartAsync();return host;
    }
    app=await Start();
    async Task<(HttpClient Client,string Token)> Browser(int person) {
        var c=app!.GetTestClient();c.BaseAddress=new Uri("https://localhost");var r=await c.GetAsync($"/synthetic-session/{person}");r.EnsureSuccessStatusCode();
        var cookies=r.Headers.GetValues("Set-Cookie").ToArray();Check(cookies.Any(x=>x.Contains("secure")&&x.Contains("httponly")),"Session cookie security flags retained");
        c.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Split(';')[0])));
        return(c,(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!);
    }
    var sender=await Browser(1);var receiver=await Browser(2);var invalid=await Browser(3);using var anon=app.GetTestClient();anon.BaseAddress=new Uri("https://localhost");
    Task<HttpResponseMessage> Post((HttpClient Client,string Token) who,string handler,params (string,string)[] values) =>
        who.Client.PostAsync("/Panel?handler="+handler,new FormUrlEncodedContent(values.Append(("__RequestVerificationToken",who.Token)).Select(x=>new KeyValuePair<string,string>(x.Item1,x.Item2))));
    async Task<HttpResponseMessage> Send(string kind,int? hours=null,byte[]? data=null,int? reply=null) {
        using var form=new MultipartFormDataContent();form.Add(new StringContent(sender.Token),"__RequestVerificationToken");form.Add(new StringContent(kind),"kind");
        if(hours!=null)form.Add(new StringContent(hours.ToString()!),"photoViewHours");if(reply!=null)form.Add(new StringContent(reply.ToString()!),"replyToMessageId");
        if(kind=="audio")form.Add(new StringContent("2"),"durationSeconds");form.Add(new ByteArrayContent(data??(kind=="photo"?jpeg:audio)),"file","synthetic.bin");
        return await sender.Client.PostAsync("/Panel?handler=Media",form);
    }
    async Task<PrivateMessage> Latest(){await using var d=new AppDbContext(opts);return await d.PrivateMessages.AsNoTracking().OrderByDescending(x=>x.Id).FirstAsync();}
    async Task<PrivateMessage> Find(int id){await using var d=new AppDbContext(opts);return await d.PrivateMessages.AsNoTracking().SingleAsync(x=>x.Id==id);}
    async Task<long> Used(){await using var d=new AppDbContext(opts);return await d.PrivateMediaQuotas.Select(x=>x.UsedBytes).SingleAsync();}
    async Task Cleanup(){await using var scope=app!.Services.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<PrivateMediaStore>().CleanupAsync(default);}
    async Task<bool> Has(int id){await using var d=new AppDbContext(opts);return await d.PrivateMediaContents.AnyAsync(x=>x.MessageId==id);}
    Check((await anon.PostAsync("/Panel?handler=Photo",new FormUrlEncodedContent(new Dictionary<string,string>{{"id","1"}}))).StatusCode==HttpStatusCode.BadRequest,"CSRF required");
    Check((await anon.GetAsync("/Panel?handler=Audio&id=1")).StatusCode==HttpStatusCode.Unauthorized,"Audio session required");
    Check((await Post(invalid,"Photo",("id","1"))).StatusCode==HttpStatusCode.Unauthorized,"Invalid private person denied");
    Check((await Post(sender,"Send",("content","Synthetic new text"))).IsSuccessStatusCode,"Text send preserved");var text=await Latest();
    Check((await Post(receiver,"Send",("content","Synthetic reply"),("replyToMessageId",text.Id.ToString()))).IsSuccessStatusCode,"Text reply preserved");
    foreach(int? hours in new int?[]{null,1,3,5}) {
        var oldUsed=await Used();Check((await Send("photo",hours,reply:text.Id)).IsSuccessStatusCode,"Photo upload committed");var m=await Latest();
        Check(m.MediaKey?.Length==32&&m.ViewedAtUtc==null&&m.PhotoAccessExpiresAtUtc==null,"MediaKey unchanged and timer not started at send");
        Check(await Has(m.Id)&&await Used()==oldUsed+jpeg.Length,"Payload durable and counted exactly");
        Check((await Post(sender,"Photo",("id",m.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Sender photo access denied");
        var response=await Post(receiver,"Photo",("id",m.Id.ToString()));Check(response.IsSuccessStatusCode&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(jpeg),"Authorized photo read");
        Check(response.Headers.CacheControl!.NoStore,"Photo no-store");var opened=await Find(m.Id);
        if(hours==null){Check(!await Has(m.Id)&&await Used()==oldUsed,"Single view removes bytes and quota atomically");Check((await Post(receiver,"Photo",("id",m.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Single view replay denied");}
        else {
            Check(opened.PhotoAccessExpiresAtUtc-opened.ViewedAtUtc==TimeSpan.FromHours(hours.Value),"Selected timer starts on first open");
            clock.Advance(TimeSpan.FromMinutes(1));Check((await Post(receiver,"Photo",("id",m.Id.ToString()))).IsSuccessStatusCode,"Timed reopen");
            Check((await Find(m.Id)).PhotoAccessExpiresAtUtc==opened.PhotoAccessExpiresAtUtc,"Timed reopen cannot extend deadline");
            clock.At=opened.PhotoAccessExpiresAtUtc!.Value;Check((await Post(receiver,"Photo",("id",m.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Exact timer expiry denied before cleanup");
            Check(await Has(m.Id),"Delayed cleanup does not grant access");await Cleanup();Check(!await Has(m.Id),"Timer expiry deletes bytea");
        }
        Check((await Find(m.Id)).ReplyToMessageId==text.Id,"Media cleanup preserves message and reply");
    }
    foreach(int? hours in new int?[]{null,1,3,5}) {
        await Send("photo",hours);var m=await Latest();clock.Advance(TimeSpan.FromHours(24));
        Check((await Post(receiver,"Photo",("id",m.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Unopened absolute 24h expiry");await Cleanup();Check(!await Has(m.Id),"Unopened content cleaned");
    }
    foreach(int hours in new[]{1,3,5}) {
        await Send("photo",hours);var m=await Latest();clock.Advance(TimeSpan.FromHours(23.5));var open=await Post(receiver,"Photo",("id",m.Id.ToString()));
        Check(open.IsSuccessStatusCode,"Late first photo opening");var v=await Find(m.Id);
        Check(v.PhotoAccessExpiresAtUtc==m.CreatedAtUtc.AddHours(24)&&double.Parse(open.Headers.GetValues("X-Photo-Remaining-Ms").Single())<=1800000,"Selected time clamped to absolute 24h limit");
        clock.At=m.CreatedAtUtc.AddHours(24);Check((await Post(receiver,"Photo",("id",m.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Active timer never survives absolute 24h");await Cleanup();
    }
    Check((await Send("photo",2)).StatusCode==HttpStatusCode.BadRequest,"Invalid photo duration rejected");
    await Send("audio");var voice=await Latest();
    using(var req=new HttpRequestMessage(HttpMethod.Get,$"/Panel?handler=Audio&id={voice.Id}")){req.Headers.Range=new(0,9);var r=await receiver.Client.SendAsync(req);Check(r.StatusCode==HttpStatusCode.PartialContent&&(await r.Content.ReadAsByteArrayAsync()).SequenceEqual(audio[..10]),"Audio 206 Range bytes");Check(r.Headers.CacheControl!.NoStore,"Audio no-store");}
    using(var req=new HttpRequestMessage(HttpMethod.Get,$"/Panel?handler=Audio&id={voice.Id}")){req.Headers.Range=new(999,1000);Check((await receiver.Client.SendAsync(req)).StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable,"Audio invalid Range 416");}
    await app.StopAsync();await app.DisposeAsync();app=await Start();sender=await Browser(1);receiver=await Browser(2);
    Check((await receiver.Client.GetAsync($"/Panel?handler=Audio&id={voice.Id}")).IsSuccessStatusCode,"Audio survives full application restart");
    Check(!Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(),"No media or key files on application disk");
    clock.At=voice.CreatedAtUtc.AddHours(24);Check((await receiver.Client.GetAsync($"/Panel?handler=Audio&id={voice.Id}")).StatusCode==HttpStatusCode.NotFound,"Audio inaccessible at exact 24h before cleanup");
    // Additional test-only failure trigger tightens deletion; production quota triggers stay enabled.
    await using(var d=new AppDbContext(opts))await d.Database.ExecuteSqlRawAsync("""
        CREATE FUNCTION public.synthetic_delete_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'SYNTHETIC_DELETE_FAILURE'; END $$;
        CREATE TRIGGER synthetic_delete_failure BEFORE DELETE ON "PrivateMediaContents" FOR EACH ROW EXECUTE FUNCTION public.synthetic_delete_failure();
        """);
    try{await Cleanup();throw new Exception("Cleanup failure not raised");}catch(Npgsql.PostgresException ex) when(ex.MessageText=="SYNTHETIC_DELETE_FAILURE"){Check(await Has(voice.Id),"Failed cleanup rolls back content deletion");}
    Check((await receiver.Client.GetAsync($"/Panel?handler=Audio&id={voice.Id}")).StatusCode==HttpStatusCode.NotFound,"Failed cleanup never restores access");
    await app.StopAsync();await app.DisposeAsync();app=await Start();sender=await Browser(1);receiver=await Browser(2);
    await using(var d=new AppDbContext(opts))await d.Database.ExecuteSqlRawAsync("DROP TRIGGER synthetic_delete_failure ON \"PrivateMediaContents\"; DROP FUNCTION public.synthetic_delete_failure()");
    await Task.WhenAll(Task.Run(Cleanup),Task.Run(Cleanup));Check(!await Has(voice.Id)&&await Used()==0,"Idempotent concurrent cleanup resumes after restart");
    await Send("photo",3);var missing=await Latest();
    await using(var d=new AppDbContext(opts))await d.PrivateMediaContents.Where(x=>x.MessageId==missing.Id).ExecuteDeleteAsync();
    Check((await Post(receiver,"Photo",("id",missing.Id.ToString()))).StatusCode==HttpStatusCode.Gone&&(await Find(missing.Id)).ViewedAtUtc==null,"Missing bytes do not consume a view or break handler");
    var listing=await receiver.Client.GetStringAsync("/Panel?handler=Messages");var listed=JsonDocument.Parse(listing).RootElement.GetProperty("messages").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==missing.Id);
    Check(!listed.GetProperty("available").GetBoolean(),"Missing payload shown as unavailable");
    await Send("photo",3);var hidden=await Latest();await using(var d=new AppDbContext(opts)){d.PrivateMessageHiddens.Add(new(){PrivateMessageId=hidden.Id,PersonNumber=2,HiddenAtUtc=clock.At});await d.SaveChangesAsync();}
    Check((await Post(receiver,"Photo",("id",hidden.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Hidden photo access denied");
    await Send("photo",3);var cleared=await Latest();await using(var d=new AppDbContext(opts))await d.PrivatePresences.Where(x=>x.PersonNumber==2).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.MessagesClearedAtUtc,clock.At));
    Check((await Post(receiver,"Photo",("id",cleared.Id.ToString()))).StatusCode==HttpStatusCode.Gone,"Cleared history access denied");
    await using(var d=new AppDbContext(opts))await d.PrivatePresences.ExecuteUpdateAsync(u=>u.SetProperty(x=>x.MessagesClearedAtUtc,(DateTime?)null));
    foreach(int? hours in new int?[]{null,1,3,5}) {
        await Send("photo",hours);var m=await Latest();var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Post(receiver,"Photo",("id",m.Id.ToString()))));
        Check(results.Count(x=>x.IsSuccessStatusCode)==(hours==null?1:4),"Concurrent PostgreSQL once/timed views");
        if(hours!=null){var viewed=await Find(m.Id);Check(viewed.PhotoAccessExpiresAtUtc-viewed.ViewedAtUtc==TimeSpan.FromHours(hours.Value),"Concurrent views initialize one deadline");}
    }
    clock.Advance(TimeSpan.FromHours(24));await Cleanup();Check(await Used()==0,"Pre-quota test cleanup releases all expired payloads");
    // Exercise 100 MiB using real logical bytes, not a test-reduced quota.
    var largeAudio=Synthetic(false,PrivateMediaStore.AudioLimit);var largeIds=new List<int>();
    for(int i=0;i<6;i++){Check((await Send("audio",data:largeAudio)).IsSuccessStatusCode,"Large synthetic audio upload");largeIds.Add((await Latest()).Id);}
    Check(await Used()==96L*1024*1024,"96 MiB accounted by database trigger");
    var four=Synthetic(true,4*1024*1024);var beforeCount=0;await using(var d=new AppDbContext(opts))beforeCount=await d.PrivateMessages.CountAsync();
    var races=await Task.WhenAll(Send("photo",data:four),Send("photo",data:four));
    Check(races.Count(x=>x.IsSuccessStatusCode)==1&&races.Count(x=>x.StatusCode==(HttpStatusCode)507)==1,"Concurrent uploads cannot exceed 100 MiB");
    Check(await Used()==PrivateMediaStore.TotalLimit,"Exactly 104857600 logical bytes maximum");
    await using(var d=new AppDbContext(opts))Check(await d.PrivateMessages.CountAsync()==beforeCount+1,"Quota rejection rolls back message insertion");
    Check((await Post(sender,"Send",("content","Text at full media quota"))).IsSuccessStatusCode,"Full quota does not block text");
    Check((await Send("photo")).StatusCode==(HttpStatusCode)507,"Full quota rejects only new media");
    Check((await receiver.Client.GetAsync($"/Panel?handler=Audio&id={largeIds[0]}")).IsSuccessStatusCode,"Existing media readable at full quota");
    // Direct insert cannot bypass database quota; existing message row is preserved by rollback.
    await using(var d=new AppDbContext(opts)) {
        await using var tx=await d.Database.BeginTransactionAsync();var m=new PrivateMessage{SenderPerson=1,SenderName="Synthetic",Kind=PrivateMessageKind.Audio,MediaKey=Guid.NewGuid().ToString("N"),CreatedAtUtc=clock.GetUtcNow().UtcDateTime};d.Add(m);await d.SaveChangesAsync();
        try {await d.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PrivateMediaContents\" (\"MessageId\",\"MediaKey\",\"Payload\",\"CreatedAtUtc\",\"ExpiresAtUtc\") VALUES ({m.Id},{m.MediaKey},{audio},{m.CreatedAtUtc},{m.CreatedAtUtc.AddHours(24)})");throw new Exception("Raw insert bypassed quota");}
        catch(Npgsql.PostgresException ex) when(ex.SqlState=="P0001"&&ex.MessageText=="PRIVATE_MEDIA_QUOTA"){Check(true,"Raw SQL inserts obey same quota");}await tx.RollbackAsync();
    }
    await using(var d=new AppDbContext(opts)) {
        var actual=await d.Database.SqlQueryRaw<long>("SELECT COALESCE(sum(octet_length(\"Payload\")),0)::bigint AS \"Value\" FROM \"PrivateMediaContents\"").SingleAsync();
        Check(actual==await Used(),"Independent octet_length sum matches quota under concurrent writes");
        try {await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PrivateMediaContents\" SET \"Payload\"=\"Payload\" WHERE \"MessageId\"={largeIds[0]}");throw new Exception("Mutable payload accepted");}
        catch(Npgsql.PostgresException ex) when(ex.MessageText=="PRIVATE_MEDIA_IMMUTABLE"){Check(true,"Stored payload cannot be overwritten");}
        try {await d.Database.ExecuteSqlRawAsync("TRUNCATE \"PrivateMediaContents\"");throw new Exception("Truncate bypass accepted");}
        catch(Npgsql.PostgresException ex) when(ex.MessageText=="PRIVATE_MEDIA_TRUNCATE_FORBIDDEN"){Check(await Used()==PrivateMediaStore.TotalLimit,"TRUNCATE cannot desynchronize quota");}
    }
    Check((await Post(sender,"Delete",("id",largeIds[0].ToString()))).IsSuccessStatusCode&&await Used()==84L*1024*1024,"FK cascade releases quota on authorized message deletion");
    Check((await Post(receiver,"Delete",("id",largeIds[1].ToString()))).StatusCode==HttpStatusCode.Forbidden,"Other person's delete denied");
    policy.Value.MaxDatabaseBytes=1;var usedBefore=await Used();Check((await Send("photo")).StatusCode==HttpStatusCode.ServiceUnavailable&&await Used()==usedBefore,"Database-size guard fails closed without deleting media");
    Check((await Post(sender,"Send",("content","Text under capacity guard"))).IsSuccessStatusCode,"Capacity guard does not block text");
    policy.Value.MaxDatabaseBytes=null;Check((await Send("photo")).StatusCode==HttpStatusCode.ServiceUnavailable,"Unknown database capacity fails closed");
    policy.Value.MaxDatabaseBytes=10L*1024*1024*1024;policy.Value.BlockNewUploads=true;Check((await Send("photo")).StatusCode==HttpStatusCode.ServiceUnavailable,"Operator disk-alarm upload kill switch");policy.Value.BlockNewUploads=false;
    Check((await Send("photo")).IsSuccessStatusCode,"Uploads recover after configured capacity restored");
    clock.Advance(TimeSpan.FromHours(24));await Task.WhenAll(Task.Run(Cleanup),Task.Run(Cleanup));
    await using(var d=new AppDbContext(opts)) {
        Check(await Used()==0&&await d.PrivateMediaContents.CountAsync()==0,"All expired content removed and quota freed");
        Check(await d.PrivateMessages.AnyAsync(x=>x.Id==text.Id&&x.Content=="Synthetic new text")&&await d.PrivateMessages.AnyAsync(x=>x.ReplyToMessageId==text.Id),"Cleanup preserves written messages and replies");
        Check(await d.CariKartlar.AnyAsync(x=>x.Id==cariId&&x.FirmaId==firmaId),"Media quota/cleanup never mutates accounting data");
        var package=await new AccountingBackupService(d,new FixtureEnvironment(root)).ExportAsync(firmaId);
        using var zip=new System.IO.Compression.ZipArchive(new MemoryStream(package));using var reader=new StreamReader(zip.GetEntry("manifest.json")!.Open());var manifest=await reader.ReadToEndAsync();
        Check(!manifest.Contains("PrivateMedia")&&!manifest.Contains("PrivateMessage"),"Accounting export does not leak private media or messages");
    }
    Check((await Send("audio")).IsSuccessStatusCode&&(await Send("photo",5)).IsSuccessStatusCode,"Automatic-cleanup restart fixtures stored");
    clock.Advance(TimeSpan.FromHours(24));
    await using(var d=new AppDbContext(opts))Check(await d.PrivateMediaContents.CountAsync()==2,"Expired bytes remain until scheduled cleanup executes");
    await app.StopAsync();await app.DisposeAsync();app=await Start(automaticCleanup:true);
    sender=await Browser(1);receiver=await Browser(2);
    var deadline=DateTime.UtcNow.AddSeconds(10);
    while(DateTime.UtcNow<deadline){await using var d=new AppDbContext(opts);if(await d.PrivateMediaContents.CountAsync()==0)break;await Task.Delay(100);}
    await using(var d=new AppDbContext(opts))Check(await d.PrivateMediaContents.CountAsync()==0&&await Used()==0,"Real hosted cleanup resumes automatically on application startup");
    Check((await Find(text.Id)).Content=="Synthetic new text","Hosted cleanup never removes written messages");
    var json=await receiver.Client.GetStringAsync("/Panel?handler=Messages");Check(!json.Contains("payload",StringComparison.OrdinalIgnoreCase)&&!json.Contains(Convert.ToBase64String(audio)),"Polling never serializes bytea payload");
    Check(!logs.Text.Contains("SYNTHETIC_MEDIA_DO_NOT_LOG")&&!logs.Text.Contains(Convert.ToBase64String(audio)),"Payload absent from application/error logs");
    Console.WriteLine($"PASS: {checks} PostgreSQL-only media checks; actual 100 MiB quota, UTC TTL, HTTP authorization, bytea, atomic transactions and restart; synthetic data only.");
} finally {
    if(app!=null)await app.DisposeAsync();if(lab!=null)await lab.DisposeAsync();
    var target=Path.GetFullPath(root);if(!target.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("pg-media-tests-"))throw new Exception("Unsafe cleanup path");Directory.Delete(target,true);
}
sealed class TestClock(DateTime at):TimeProvider {public DateTime At=at;public override DateTimeOffset GetUtcNow()=>new(DateTime.SpecifyKind(At,DateTimeKind.Utc));public void Advance(TimeSpan d)=>At=At.Add(d);}
sealed class MutableOptions:IOptionsMonitor<PrivateMediaOptions> {public PrivateMediaOptions Value=new(){MaxDatabaseBytes=10L*1024*1024*1024};public PrivateMediaOptions CurrentValue=>Value;public PrivateMediaOptions Get(string? name)=>Value;public IDisposable? OnChange(Action<PrivateMediaOptions,string?> listener)=>null;}
sealed class FixtureEnvironment(string root):IWebHostEnvironment {
    public string ApplicationName{get;set;}="Synthetic";public string EnvironmentName{get;set;}="Development";public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=Path.Combine(root,"wwwroot");
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider{get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider{get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
}
sealed class LogCapture:ILoggerProvider {
    readonly System.Collections.Concurrent.ConcurrentQueue<string> entries=new();public string Text=>string.Join("\n",entries);public ILogger CreateLogger(string category)=>new Capture(this);public void Dispose(){}
    sealed class Capture(LogCapture owner):ILogger {public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel l)=>true;public void Log<TState>(LogLevel l,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> format){owner.entries.Enqueue(format(state,ex));while(owner.entries.Count>2000)owner.entries.TryDequeue(out _);}}
}
